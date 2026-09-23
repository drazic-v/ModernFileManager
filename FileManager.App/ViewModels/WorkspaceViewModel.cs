using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
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

    public WorkspaceViewModel(IStorageProvider provider, StoragePath startingFolder, string displayName, INotificationService notifications, IConflictResolutionService conflictResolution)
    {
        _notifications = notifications;
        _conflictResolution = conflictResolution;
        Tabs = new ObservableCollection<MainViewModel>();

        AddTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.Create<MainViewModel>(CloseTab);

        // This is just a placeholder to demonstrate how the transfer progress bar works.
        // In a real application, you would add TransferViewModel instances to
        // ActiveTransfers when actual file transfers are initiated.
        //ActiveTransfers.Add(new TransferViewModel("example.zip") { ProgressPercent = 42 });

        Providers.Add(new ProviderViewModel(displayName, provider, startingFolder));
        OpenProviderCommand = ReactiveCommand.Create<ProviderViewModel>(entry => OpenTab(entry.Provider, entry.StartingFolder, entry.DisplayName));
        AddProviderCommand = ReactiveCommand.Create(() => { /* TODO: open a connect-provider window once a second provider type exists */ });

        PasteCommand = ReactiveCommand.CreateFromTask(PasteAsync,
            this.WhenAnyValue(x => x.Clipboard, x => x.SelectedTab,
                (clip, tab) => clip is not null && tab is not null && clip.SourceProvider.ProviderId == tab.Provider.ProviderId));
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

    private static bool IsApplicable(NameCollisionPolicy policy, StorageItemKind conflictingKind) =>
    policy != NameCollisionPolicy.Merge || conflictingKind == StorageItemKind.Directory;

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clip || SelectedTab is not { } target) return;
        if (clip.SourceProvider.ProviderId != target.Provider.ProviderId)
            return; // TODO: route through TransferManager's stream-pump path once it exists

        var itemsToProcess = new List<StorageItem>();
        foreach (var item in clip.Items)
        {
            if (clip.IsCut && item.Path.Parent() is { } itemParent && StoragePath.PathsEqual(itemParent, target.CurrentFolder))
            {
                _notifications.ShowError($"Can't move \"{item.Name}\" to its original location.");
                continue;
            }

            if (item.Kind == StorageItemKind.Directory && StoragePath.PathsEqual(target.CurrentFolder, item.Path))
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

        var conflictTracker = new PasteConflictTracker(_conflictResolution);
        ConflictResolver resolver = conflictTracker.ResolveAsync;

        var succeeded = new List<StorageItem>();
        var skipped = new List<StorageItem>();
        var failed = new List<string>();

        try
        {
            transfer.IsMeasuring = true;
            var itemSizes = new List<long>();
            foreach (var item in itemsToProcess)
            {
                itemSizes.Add(item.Kind == StorageItemKind.Directory
                    ? (await FolderInfoCalculator.GetFolderInfo(clip.SourceProvider, item.Path, ct: transfer.Token)).Size
                    : item.SizeInBytes ?? 0);
            }
            var totalBytes = itemSizes.Sum();
            transfer.IsMeasuring = false;

            long offsetBytes = 0;
            for (var i = 0; i < itemsToProcess.Count; i++)
            {
                var item = itemsToProcess[i];
                conflictTracker.CurrentItem = item;
                var itemOffset = offsetBytes;

                var progress = new Progress<TransferProgress>(p =>
                    transfer.ProgressPercent = totalBytes > 0 ? Math.Min(100, (double)(itemOffset + p.BytesCopied) / totalBytes * 100) : 100);

                try
                {
                    if (clip.IsCut)
                        await clip.SourceProvider.MoveAsync(item.Path, target.CurrentFolder, resolver, progress, transfer.Token);
                    else
                        await clip.SourceProvider.CopyAsync(item.Path, target.CurrentFolder, resolver, progress, transfer.Token);

                    if (conflictTracker.ItemResolutions.TryGetValue(item, out var resolution) && resolution == NameCollisionPolicy.Skip)
                        skipped.Add(item);
                    else
                        succeeded.Add(item);
                }
                catch (OperationCanceledException)
                {
                    throw; // cancellation stops the whole batch, unlike a per-item failure
                }
                catch (Exception ex)
                {
                    failed.Add($"{item.Name}: {ex.Message}");
                }

                offsetBytes += itemSizes[i];
                transfer.ProgressPercent = totalBytes > 0 ? Math.Min(100, (double)offsetBytes / totalBytes * 100) : 100;
            }

            if (clip.IsCut) Clipboard = null;

            var verb = clip.IsCut ? "moved" : "copied";
            var parts = new List<string>();

            if (succeeded.Count > 0)
                parts.Add(succeeded.Count == 1 ? $"{verb} \"{succeeded[0].Name}\"" : $"{verb} {succeeded.Count} items");
            if (skipped.Count > 0)
                parts.Add(skipped.Count == 1 ? $"skipped \"{skipped[0].Name}\" (already exists)" : $"skipped {skipped.Count} items (already exist)");
            if (failed.Count > 0)
                parts.Add(failed.Count == 1 ? "1 failed" : $"{failed.Count} failed");

            var message = string.Join(", ", parts);
            message = char.ToUpper(message[0]) + message[1..] + ".";

            if (failed.Count > 0)
                _notifications.ShowError($"{message} First error: {failed[0]}");
            else
                _notifications.ShowSuccess(message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ActiveTransfers.Remove(transfer);
            transfer.Dispose();
        }

        await target.RefreshAsync();
        await RefreshTabsViewingAsync(target, target.CurrentFolder);

        if (clip.IsCut && clip.Items[0].Path.Parent() is { } sourceParent)
            await RefreshTabsViewingAsync(target, sourceParent);
    }
}