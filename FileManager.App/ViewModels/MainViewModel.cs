using FileManager.App.Services;
using FileManager.Core.Models;
using FileManager.Core.Providers;
using FileManager.Infrastructure.Providers;
using ReactiveUI;

namespace FileManager.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    public MainViewModel(IStorageProvider provider, StoragePath startingFolder, string displayName, INotificationService notifications)
    : base(provider, startingFolder, displayName, notifications) { }
}
