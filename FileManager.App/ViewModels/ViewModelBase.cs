using Avalonia.Threading;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Pipes;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace FileManager.App.ViewModels;

public abstract class ViewModelBase : ReactiveObject, IDisposable
{
    private bool _showHiddenItems;
    private bool _canGoBack;
    private bool _isSearchActive;

    private bool _canGoForward;
    private readonly string _displayName;
    private string _searchText = string.Empty;
    private CancellationTokenSource? _currentOperationCts;

    private readonly IStorageProvider _provider;
    private StoragePath _currentFolder;
    private StorageItem? _selectedItem;
    private readonly Stack<StoragePath> _backStack = new();
    private readonly Stack<StoragePath> _forwardStack = new();

    public ObservableCollection<StorageItem> Items { get; } = new();

    public IStorageProvider Provider => _provider;
    public string DisplayName => _displayName;
    private readonly ObservableAsPropertyHelper<string> _tabName;
    public string TabName => _tabName.Value;
    private readonly ObservableAsPropertyHelper<string> _displayPath;
    public string DisplayPath => _displayPath.Value;

    private long? _folderSizeInBytes;
    private int? _folderFileCount;
    private int? _folderFolderCount;
    private bool _isFolderInfoLoading;
    private CancellationTokenSource? _folderInfoCts;

    public long? FolderSizeInBytes { get => _folderSizeInBytes; private set => this.RaiseAndSetIfChanged(ref _folderSizeInBytes, value); }
    public int? FolderFileCount { get => _folderFileCount; private set => this.RaiseAndSetIfChanged(ref _folderFileCount, value); }
    public int? FolderFolderCount { get => _folderFolderCount; private set => this.RaiseAndSetIfChanged(ref _folderFolderCount, value); }
    public bool IsFolderInfoLoading { get => _isFolderInfoLoading; private set => this.RaiseAndSetIfChanged(ref _isFolderInfoLoading, value); }

    private bool _isDetailsPanelOpen = false;

    public bool IsDetailsPanelOpen
    {
        get => _isDetailsPanelOpen;
        set
        {
            this.RaiseAndSetIfChanged(ref _isDetailsPanelOpen, value);
            _ = UpdateFolderInfoAsync();
        }
    }

    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        internal set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    public StoragePath CurrentFolder
    {
        get => _currentFolder;
        private set => this.RaiseAndSetIfChanged(ref _currentFolder, value);
    }

    public StorageItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            _ = UpdateFolderInfoAsync();
        }
    }

    public bool ShowHiddenItems
    {
        get => _showHiddenItems;
        set
        {
            this.RaiseAndSetIfChanged(ref _showHiddenItems, value);
            _ = RefreshAsync();
        }
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => this.RaiseAndSetIfChanged(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => this.RaiseAndSetIfChanged(ref _canGoForward, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    public bool IsSearchActive
    {
        get => _isSearchActive;
        private set => this.RaiseAndSetIfChanged(ref _isSearchActive, value);
    }

    public ReactiveCommand<Unit, Unit> NavigateUpCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<StorageItem, Unit> OpenItemCommand { get; }

    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder, string displayName)
    {
        _showHiddenItems = false;
        _provider = provider;
        _currentFolder = startingFolder;
        _displayName = displayName;
        _tabName = this.WhenAnyValue(x => x.CurrentFolder).Select(folder => $"{_displayName}: {folder.Name}").ToProperty(this, x => x.TabName);
        _displayPath = this.WhenAnyValue(x => x.CurrentFolder).Select(folder => TruncatePath(folder.Value, 50)).ToProperty(this, x => x.DisplayPath);
        NavigateUpCommand = ReactiveCommand.CreateFromTask(NavigateUpAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        BackCommand = ReactiveCommand.CreateFromTask(BackAsync, this.WhenAnyValue(x => x.CanGoBack));
        ForwardCommand = ReactiveCommand.CreateFromTask(ForwardAsync, this.WhenAnyValue(x => x.CanGoForward));
        ClearSearchCommand = ReactiveCommand.CreateFromTask(ClearSearchAsync);
        OpenItemCommand = ReactiveCommand.CreateFromTask<StorageItem>(OpenItemAsync);
        _ = LoadAsync(startingFolder);
    }

    private CancellationToken BeginNewOperation()
    {
        _currentOperationCts?.Cancel();
        _currentOperationCts?.Dispose();
        _currentOperationCts = new CancellationTokenSource();
        return _currentOperationCts.Token;
    }

    private async Task LoadAsync(StoragePath folder)
    {
        var token = BeginNewOperation();
        var hasCleared = false;
        try
        {
            await foreach (var item in _provider.ListAsync(folder, token))
            {
                if (!hasCleared)
                {
                    Items.Clear();
                    SelectedItem = null;
                    hasCleared = true;
                }
                if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
                    Items.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return; // hasCleared is still false here on an early failure - current listing untouched
        }

        if (!hasCleared)
        {
            // loop completed with zero items - a genuinely empty folder still needs to *look* empty
            Items.Clear();
            SelectedItem = null;
        }

        CurrentFolder = folder;
        IsSearchActive = false;
    }

    private void UpdateNavigationState()
    {
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    private async Task NavigateToAsync(StoragePath folder)
    {
        var previous = CurrentFolder;
        await LoadAsync(folder);

        if (!StoragePath.PathsEqual(CurrentFolder, folder))
            return; // LoadAsync failed - CurrentFolder never actually changed, nothing to record

        _backStack.Push(previous);
        _forwardStack.Clear();
        UpdateNavigationState();
    }

    public async Task NavigateIntoAsync(StorageItem item)
    {
        if (item.IsFolder)
            await NavigateToAsync(item.Path);
    }

    public async Task NavigateUpAsync()
    {
        if (CurrentFolder.Parent() is { } parent)
            await NavigateToAsync(parent);
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(CurrentFolder);
    }

    public async Task BackAsync()
    {
        if (_backStack.Count == 0) return;
        var target = _backStack.Peek();
        var before = CurrentFolder;
        await LoadAsync(target);

        if (!StoragePath.PathsEqual(CurrentFolder, target))
            return; // leave the stacks untouched - the entry might still be valid later

        _backStack.Pop();
        _forwardStack.Push(before);
        UpdateNavigationState();
    }

    public async Task ForwardAsync()
    {
        if (_forwardStack.Count == 0) return;
        var target = _forwardStack.Peek();
        var before = CurrentFolder;
        await LoadAsync(target);

        if (!StoragePath.PathsEqual(CurrentFolder, target))
            return;

        _forwardStack.Pop();
        _backStack.Push(before);
        UpdateNavigationState();
    }

    public async Task SearchCurrentFolderAsync(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            await LoadAsync(CurrentFolder);
            return;
        }

        var token = BeginNewOperation();
        Items.Clear();
        IsSearchActive = true;
        await Dispatcher.Yield(DispatcherPriority.Background); // let the Cancel button actually paint before the heavy work starts

        var count = 0;
        try
        {
            await foreach (var item in _provider.SearchAsync(CurrentFolder, query, token))
            {
                if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
                    Items.Add(item);

                if (++count % 25 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return; // same reasoning as LoadAsync: fail safely instead of crashing.
        }
    }

    public async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        await LoadAsync(CurrentFolder);
    }

    private async Task UpdateFolderInfoAsync()
    {
        _folderInfoCts?.Cancel();
        _folderInfoCts?.Dispose();
        _folderInfoCts = null;

        FolderSizeInBytes = null;
        FolderFileCount = null;
        FolderFolderCount = null;

        if (!IsDetailsPanelOpen || SelectedItem is not { Kind: StorageItemKind.Directory } folder)
            return;

        _folderInfoCts = new CancellationTokenSource();
        var token = _folderInfoCts.Token;
        IsFolderInfoLoading = true;

        var progress = new Progress<FolderInfoCalculator.FolderInfo>(info =>
        {
            FolderSizeInBytes = info.Size;
            FolderFileCount = info.Files;
            FolderFolderCount = info.Folders;
        });

        await Dispatcher.Yield(DispatcherPriority.Background); // let the Cancel button actually paint before the heavy work starts

        try
        {
            await Task.Run(() => FolderInfoCalculator.GetFolderInfo(_provider, folder.Path, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            return; // a newer selection superseded this calculation
        }
        catch (Exception)
        {
            // same reasoning as LoadAsync: fail safely instead of crashing.
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsFolderInfoLoading = false;
        }
    }

    private static string TruncatePath(string path, int maxLength)
    {
        if (path.Length <= maxLength) return path;
        return "..." + path[^maxLength..];
    }
    public async Task OpenItemAsync(StorageItem item)
    {
        if (item.Kind == StorageItemKind.Directory)
        {
            await NavigateIntoAsync(item);
            return;
        }
        try
        {
            await _provider.OpenFileAsync(item.Path);
        }
        catch (Exception)
        {
            // same reasoning as LoadAsync: fail safely instead of crashing.
        }
    }
    public async Task DeleteItemAsync(StorageItem item)
    {
        try
        {
            await _provider.DeleteAsync(item.Path);
            Items.Remove(item);
            if (SelectedItem == item) SelectedItem = null;
        }
        catch (Exception)
        {
            return; // same reasoning as LoadAsync: fail safely instead of crashing.
        }
        
    }

    public async Task<StorageItem?> CreateFolderAsync()
    {
        try
        {
            var created = await _provider.CreateDirectoryAsync(CurrentFolder, "New Folder");
            Items.Add(created);
            SelectedItem = created;
            return created;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task RenameItemAsync(StorageItem item, string newName)
    {
        try
        {
            var renamed = await _provider.RenameAsync(item.Path, newName);
            var index = Items.IndexOf(item);
            if (index >= 0) Items[index] = renamed;
            if (SelectedItem == item) SelectedItem = renamed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _tabName.Dispose();
        _currentOperationCts?.Cancel();
        _currentOperationCts?.Dispose();
        _folderInfoCts?.Cancel();
        _folderInfoCts?.Dispose();
    }
}
