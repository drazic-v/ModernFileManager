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
    public ViewModelBase(IStorageProvider provider, StoragePath startingFolder)
    {
        _provider = provider;
        _currentFolder = startingFolder;
        NavigateUpCommand = ReactiveCommand.CreateFromTask(NavigateUpAsync);
        _ = LoadAsync(startingFolder);
    }

    private async Task LoadAsync(StoragePath folder)
    {
        Items.Clear();
        await foreach (var item in _provider.ListAsync(folder))
            Items.Add(item);
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
}
