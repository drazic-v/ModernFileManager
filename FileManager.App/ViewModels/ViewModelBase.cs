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

public abstract class ViewModelBase : ReactiveObject
{
    private bool _showHiddenItems;
    private bool _canGoBack;
    private bool _isSearchActive;

    private bool _canGoForward;
    private string _searchText = string.Empty;
    private CancellationTokenSource? _currentOperationCts;

    private readonly IStorageProvider _provider;
    private StoragePath _currentFolder;
    private StorageItem? _selectedItem;
    private readonly Stack<StoragePath> _backStack = new();
    private readonly Stack<StoragePath> _forwardStack = new();

    public ObservableCollection<StorageItem> Items { get; } = new();

    private readonly ObservableAsPropertyHelper<string> _tabName;
    public string TabName => _tabName.Value;

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
    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder)
    {
        _showHiddenItems = false;
        _provider = provider;
        _currentFolder = startingFolder;
        _tabName = this.WhenAnyValue(x => x.CurrentFolder).Select(folder => $"{_provider.ProviderId}: {folder.Name}").ToProperty(this, x => x.TabName);
        NavigateUpCommand = ReactiveCommand.CreateFromTask(NavigateUpAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        BackCommand = ReactiveCommand.CreateFromTask(BackAsync, this.WhenAnyValue(x => x.CanGoBack));
        ForwardCommand = ReactiveCommand.CreateFromTask(ForwardAsync, this.WhenAnyValue(x => x.CanGoForward));
        ClearSearchCommand = ReactiveCommand.CreateFromTask(ClearSearchAsync);
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
        Items.Clear();
        try
        {
            await foreach (var item in _provider.ListAsync(folder, token))
            {
                if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
                    Items.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            return; // something newer superseded this call — let that one finish instead
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
        _backStack.Push(CurrentFolder);
        _forwardStack.Clear();
        UpdateNavigationState();
        await LoadAsync(folder);
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
        _forwardStack.Push(CurrentFolder);
        var previous = _backStack.Pop();
        UpdateNavigationState();
        await LoadAsync(previous);
    }

    public async Task ForwardAsync()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(CurrentFolder);
        var next = _forwardStack.Pop();
        UpdateNavigationState();
        await LoadAsync(next);
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
    }

    public async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        await LoadAsync(CurrentFolder);
    }
}
