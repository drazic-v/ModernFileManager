using FileManager.Infrastructure.Providers;
using FileManager.Core.Providers;
using FileManager.Core.Models;
using ReactiveUI;

namespace FileManager.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    public MainViewModel(IStorageProvider provider, StoragePath startingFolder) 
        : base(provider, startingFolder) { }
}
