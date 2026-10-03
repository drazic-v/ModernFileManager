using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Core.Transfers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace FileManager.App.ViewModels;

public class WorkspaceViewModel : ReactiveObject
{
    private MainViewModel? _selectedTab;
    public ObservableCollection<TransferViewModel> ActiveTransfers { get; } = new();

    private ClipboardEntry? _clipboard;
    public ClipboardEntry? Clipboard
    {
        get => _clipboard;
        private set => this.RaiseAndSetIfChanged(ref _clipboard, value);
    }

    public ReactiveCommand<Unit, Unit> PasteCommand { get; }

    private readonly INotificationService _notifications;
    private readonly IConflictResolutionService _conflictResolution;
    private readonly TransferManager _transfers;

    public WorkspaceViewModel(IStorageProvider provider, StoragePath startingFolder, string displayName,
        INotificationService notifications, IConflictResolutionService conflictResolution, TransferManager transfers)
    {
        _notifications = notifications;
        _conflictResolution = conflictResolution;
        _transfers = transfers;
        Tabs = new ObservableCollection<MainViewModel>();

        AddTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.Create<MainViewModel>(CloseTab);

        Providers.Add(new ProviderViewModel(displayName, provider, startingFolder));
        OpenProviderCommand = ReactiveCommand.Create<ProviderViewModel>(entry => OpenTab(entry.Provider, entry.StartingFolder, entry.DisplayName));
        AddProviderCommand = ReactiveCommand.Create(() => { /* TODO: open a connect-provider window once a second provider type exists */ });

        // Cross-provider paste is allowed now - the manager's stream-pump path handles it.
        PasteCommand = ReactiveCommand.CreateFromTask(PasteAsync,
            this.WhenAnyValue(x => x.Clipboard, x => x.SelectedTab,
                (clip, tab) => clip is not null && tab is not null));
    }

    public ObservableCollection<MainViewModel> Tabs { get; }

    public ObservableCollection<ProviderViewModel> Providers { get; } = new();

    public MainViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            if (_selectedTab is not null) _selectedTab.IsActive = false;
            this.RaiseAndSetIfChanged(ref _selectedTab, value);
            if (_selectedTab is not null) _selectedTab.IsActive = true;
        }
    }

    public ReactiveCommand<Unit, Unit> AddTabCommand { get; }
    public ReactiveCommand<MainViewModel, Unit> CloseTabCommand { get; }

    public ReactiveCommand<ProviderViewModel, Unit> OpenProviderCommand { get; }
    public ReactiveCommand<Unit, Unit> AddProviderCommand { get; }

    private void AddTab()
    {
        if (SelectedTab is { } current)
            OpenTab(current.Provider, current.CurrentFolder, current.DisplayName);
        else
            OpenTab(Providers[0].Provider, Providers[0].StartingFolder, Providers[0].DisplayName);
    }

    private void OpenTab(IStorageProvider provider, StoragePath startingFolder, string displayName)
    {
        var tab = new MainViewModel(provider, startingFolder, displayName, _notifications);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseTab(MainViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var wasSelected = SelectedTab == tab;

        Tabs.Remove(tab);
        tab.Dispose();

        if (wasSelected)
            SelectedTab = Tabs.Count > 0 ? Tabs[Math.Max(0, index - 1)] : null;
    }

    public async Task RefreshTabsViewingAsync(MainViewModel exclude, StoragePath folder)
    {
        foreach (var tab in Tabs.Where(t => t != exclude && StoragePath.PathsEqual(t.CurrentFolder, folder)))
            await tab.RefreshAsync();
    }

    public void SetClipboard(IReadOnlyList<StorageItem> items, IStorageProvider provider, bool isCut)
    {
        if (items.Count == 0) return;
        Clipboard = new ClipboardEntry(items, provider, isCut);
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clip || SelectedTab is not { } target) return;

        // These guards compare paths, so they only make sense within one provider.
        var sameProvider = clip.SourceProvider.ProviderId == target.Provider.ProviderId;

        var itemsToProcess = new List<StorageItem>();
        foreach (var item in clip.Items)
        {
            if (sameProvider && clip.IsCut && item.Path.Parent() is { } itemParent
                && StoragePath.PathsEqual(itemParent, target.CurrentFolder))
            {
                _notifications.ShowError($"Can't move \"{item.Name}\" to its original location.");
                continue;
            }

            // Blocks pasting a folder into itself AND into any of its own subfolders.
            if (sameProvider && item.Kind == StorageItemKind.Directory
                && StoragePath.IsSameOrDescendant(target.CurrentFolder, item.Path))
            {
                _notifications.ShowError($"Can't paste \"{item.Name}\" into itself.");
                continue;
            }

            itemsToProcess.Add(item);
        }

        if (itemsToProcess.Count == 0)
        {
            if (clip.IsCut) Clipboard = null;
            return;
        }

        var transfer = new TransferViewModel(itemsToProcess.Count == 1 ? itemsToProcess[0].Name : $"{itemsToProcess.Count} items");
        ActiveTransfers.Add(transfer);

        var submittedIds = new List<Guid>();
        try
        {
            // Measure up front so the aggregate percent is meaningful from the first update.
            transfer.IsMeasuring = true;
            var sizes = new List<long>();
            foreach (var item in itemsToProcess)
            {
                sizes.Add(item.Kind == StorageItemKind.Directory
                    ? (await FolderInfoCalculator.GetFolderInfo(clip.SourceProvider, item.Path, ct: transfer.Token)).Size
                    : item.SizeInBytes ?? 0);
            }
            transfer.IsMeasuring = false;

            var tracker = new PasteConflictTracker(_conflictResolution, onAbort: transfer.Cancel);
            var batch = new PasteBatch(itemsToProcess, sizes, onPercent: percent => transfer.ProgressPercent = percent);

            for (var i = 0; i < itemsToProcess.Count; i++)
            {
                var item = itemsToProcess[i];
                var request = new TransferRequest
                {
                    SourceProvider = clip.SourceProvider,
                    SourcePath = item.Path,
                    DestinationProvider = target.Provider,
                    DestinationFolder = target.CurrentFolder,
                    Operation = clip.IsCut ? TransferOperation.Move : TransferOperation.Copy,
                    ConflictResolver = tracker.ForItem(item)
                };

                // Created here, on the UI thread, so Progress<T> marshals updates back to it.
                var id = _transfers.Submit(request, batch.CreateProgress(i));
                batch.Entries[i].TransferId = id;
                submittedIds.Add(id);
            }

            // Cancel button (or an abandoned conflict dialog) => cancel every submitted job.
            // Registered after the list is complete; if already cancelled, this fires immediately.
            using var cancelBridge = transfer.Token.Register(() =>
            {
                foreach (var id in submittedIds) _transfers.Cancel(id);
            });

            await batch.WhenAllFinished;

            // Cancellation stays silent and keeps the clipboard, as before.
            if (batch.Entries.All(e => e.Status != TransferStatus.Cancelled))
            {
                if (clip.IsCut) Clipboard = null;
                ShowPasteSummary(clip.IsCut, batch.Entries);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled while measuring - nothing was submitted.
        }
        finally
        {
            foreach (var id in submittedIds) _transfers.Forget(id);   // drop retained Failed jobs; nothing retries them yet
            ActiveTransfers.Remove(transfer);
            transfer.Dispose();
        }

        await target.RefreshAsync();
        await RefreshTabsViewingAsync(target, target.CurrentFolder);

        if (clip.IsCut && clip.Items[0].Path.Parent() is { } sourceParent)
            await RefreshTabsViewingAsync(target, sourceParent);
    }

    private void ShowPasteSummary(bool isCut, IReadOnlyList<PasteBatch.Entry> entries)
    {
        var succeeded = entries.Where(e => e.Status == TransferStatus.Succeeded).ToList();
        var skipped = entries.Where(e => e.Status == TransferStatus.Skipped).ToList();
        var failed = entries.Where(e => e.Status == TransferStatus.Failed).ToList();

        var verb = isCut ? "moved" : "copied";
        var parts = new List<string>();

        if (succeeded.Count > 0)
            parts.Add(succeeded.Count == 1 ? $"{verb} \"{succeeded[0].Item.Name}\"" : $"{verb} {succeeded.Count} items");
        if (skipped.Count > 0)
            parts.Add(skipped.Count == 1 ? $"skipped \"{skipped[0].Item.Name}\" (already exists)" : $"skipped {skipped.Count} items (already exist)");
        if (failed.Count > 0)
            parts.Add(failed.Count == 1 ? "1 failed" : $"{failed.Count} failed");

        var message = string.Join(", ", parts);
        message = char.ToUpper(message[0]) + message[1..] + ".";

        if (failed.Count > 0)
            _notifications.ShowError($"{message} First error: {failed[0].Item.Name}: {failed[0].Error?.Message}");
        else
            _notifications.ShowSuccess(message);
    }
}