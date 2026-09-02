using FileManager.Core.Models;
using FileManager.Core.Providers;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Pipes;
using System.Reactive;
using System.Threading.Tasks;
namespace FileManager.App.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    private bool _showHiddenItems;
    private bool _canGoBack;
    private bool _canGoForward;
    private readonly IStorageProvider _provider;
    private StoragePath _currentFolder;
    private StorageItem? _selectedItem;
    private readonly Stack<StoragePath> _backStack = new();
    private readonly Stack<StoragePath> _forwardStack = new();

    public ObservableCollection<StorageItem> Items { get; } = new();

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

    public ReactiveCommand<Unit, Unit> NavigateUpCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ForwardCommand { get; }
    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder)
    {
        _showHiddenItems = false;
        _provider = provider;
        _currentFolder = startingFolder;
        NavigateUpCommand = ReactiveCommand.CreateFromTask(NavigateUpAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        BackCommand = ReactiveCommand.CreateFromTask(BackAsync, this.WhenAnyValue(x => x.CanGoBack));
        ForwardCommand = ReactiveCommand.CreateFromTask(ForwardAsync, this.WhenAnyValue(x => x.CanGoForward));
        _ = LoadAsync(startingFolder);
    }

    private async Task LoadAsync(StoragePath folder)
    {
        Items.Clear();
        await foreach (var item in _provider.ListAsync(folder))
        {
            if (_showHiddenItems || !StorageItemFilters.IsHidden(item))
            {
                Items.Add(item);
            }
        }
        CurrentFolder = folder;
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
}
