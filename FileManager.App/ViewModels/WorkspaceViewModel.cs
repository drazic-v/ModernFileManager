using FileManager.Core.Models;
using FileManager.Core.Providers;
using ReactiveUI;
using System;
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

    public ReactiveCommand<StorageItem, Unit> CopyToClipboardCommand { get; }
    public ReactiveCommand<StorageItem, Unit> CutToClipboardCommand { get; }
    public ReactiveCommand<Unit, Unit> PasteCommand { get; }

    public WorkspaceViewModel(IStorageProvider provider, StoragePath startingFolder, string displayName)
    {
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

        CopyToClipboardCommand = ReactiveCommand.Create<StorageItem>(item => SetClipboard(item, isCut: false));
        CutToClipboardCommand = ReactiveCommand.Create<StorageItem>(item => SetClipboard(item, isCut: true));
        PasteCommand = ReactiveCommand.CreateFromTask(PasteAsync,
            this.WhenAnyValue(x => x.Clipboard, x => x.SelectedTab,
                (clip, tab) => clip is not null && tab is not null && clip.SourceProvider == tab.Provider));
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
        var tab = new MainViewModel(provider, startingFolder, displayName);
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

    private void SetClipboard(StorageItem item, bool isCut)
    {
        var provider = Providers.FirstOrDefault(p => p.Provider.ProviderId == item.Path.ProviderId)?.Provider;
        if (provider is null) return;
        Clipboard = new ClipboardEntry(item, provider, isCut);
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clip || SelectedTab is not { } target) return;

        if (clip.SourceProvider.ProviderId != target.Provider.ProviderId)
        {
            return; // TODO: route through TransferManager's stream-pump path once it exists
        }

        if (clip.IsCut && clip.Item.Path.Parent() is { } sourceParent && StoragePath.PathsEqual(sourceParent, target.CurrentFolder))
        {
            Clipboard = null; // already exactly here - nothing to do
            return;
        }

        if (clip.Item.Kind == StorageItemKind.Directory && StoragePath.IsSameOrDescendant(target.CurrentFolder, clip.Item.Path))
        {
            return; // TODO: real user-facing "can't paste a folder into itself" message once we build error surfacing
        }

        var transfer = new TransferViewModel(clip.Item.Name);
        ActiveTransfers.Add(transfer);

        try
        {
            long totalBytes = clip.Item.Kind == StorageItemKind.Directory
                ? (await FolderInfoCalculator.GetFolderInfo(clip.SourceProvider, clip.Item.Path, ct: transfer.Token)).Size
                : clip.Item.SizeInBytes ?? 0;

            transfer.IsMeasuring = false;

            var progress = new Progress<TransferProgress>(p =>
                transfer.ProgressPercent = totalBytes > 0 ? Math.Min(100, (double)p.BytesCopied / totalBytes * 100) : 100);

            if (clip.IsCut)
                await clip.SourceProvider.MoveAsync(clip.Item.Path, target.CurrentFolder, progress: progress, ct: transfer.Token);
            else
                await clip.SourceProvider.CopyAsync(clip.Item.Path, target.CurrentFolder, progress: progress, ct: transfer.Token);

            if (clip.IsCut) Clipboard = null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // TODO: surface a real error once there's a notification system; for now, fail without crashing
        }
        finally
        {
            ActiveTransfers.Remove(transfer);
            transfer.Dispose();
        }

        await target.RefreshAsync();
    }
}