using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using System.Reactive;
namespace FileManager.App.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    private bool _showHiddenItems;
    private readonly IStorageProvider _provider;
    private StoragePath _currentFolder;
    private StorageItem? _selectedItem;
    
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

    public ReactiveCommand<Unit, Unit> NavigateUpCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder)
    {
        _showHiddenItems = false;
        _provider = provider;
        _currentFolder = startingFolder;
        NavigateUpCommand = ReactiveCommand.CreateFromTask(NavigateUpAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
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

    public async Task NavigateIntoAsync(StorageItem item)
    {
        if (item.IsFolder)
            await LoadAsync(item.Path);
    }

    public async Task NavigateUpAsync()
    {
        if (CurrentFolder.Parent() is { } parent)
            await LoadAsync(parent);
    }

    public async Task RefreshAsync()
    {
        await LoadAsync(CurrentFolder);
    }
}
