using Avalonia.Threading;
using DynamicData.Kernel;
using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO.Pipes;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace FileManager.App.ViewModels;

public abstract partial class ViewModelBase : ReactiveObject, IDisposable
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
    public ObservableCollection<StorageItem> SelectedItems { get; } = new();
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


    private long? _multiSelectionSizeBytes;
    public long? MultiSelectionSizeBytes { get => _multiSelectionSizeBytes; private set => this.RaiseAndSetIfChanged(ref _multiSelectionSizeBytes, value); }

    private bool _isMultiSelectionSizeLoading;
    public bool IsMultiSelectionSizeLoading { get => _isMultiSelectionSizeLoading; private set => this.RaiseAndSetIfChanged(ref _isMultiSelectionSizeLoading, value); }

    private CancellationTokenSource? _multiSelectionCts;

    private bool _isDetailsPanelOpen = false;

    public bool IsDetailsPanelOpen
    {
        get => _isDetailsPanelOpen;
        set
        {
            this.RaiseAndSetIfChanged(ref _isDetailsPanelOpen, value);
            _ = UpdateFolderInfoAsync();
            _ = UpdateMultiSelectionInfoAsync();
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
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
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

    private readonly INotificationService _notifications;


    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder, string displayName, INotificationService notifications)
    {
        _showHiddenItems = false;
        _provider = provider;
        _currentFolder = startingFolder;
        _displayName = displayName;
        _notifications = notifications;
        SelectedItems.CollectionChanged += OnSelectedItemsChanged;
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
    
    private static string TruncatePath(string path, int maxLength)
    {
        if (path.Length <= maxLength) return path;
        return "..." + path[^maxLength..];
    }

    public void Dispose()
    {
        _tabName.Dispose();
        _currentOperationCts?.Cancel();
        _currentOperationCts?.Dispose();
        _folderInfoCts?.Cancel();
        _folderInfoCts?.Dispose();
        _multiSelectionCts?.Cancel();
        _multiSelectionCts?.Dispose();
    }
}
