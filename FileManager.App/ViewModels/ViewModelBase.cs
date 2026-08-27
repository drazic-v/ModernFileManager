using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FileManager.Core.Models;
using FileManager.Core.Providers;
namespace FileManager.App.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    private readonly IStorageProvider _provider;

    public ObservableCollection<StorageItem> Items { get; } = new();

    public ViewModelBase(IStorageProvider provider)
    {
        _provider = provider;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Items.Clear();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        var path = new StoragePath { ProviderId = "local", Value = home };

        await foreach (var item in _provider.ListAsync(path))
        {
            Items.Add(item);
        }
    }
}
