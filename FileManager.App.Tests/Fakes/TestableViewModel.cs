using FileManager.App.Services;
using FileManager.App.ViewModels;
using FileManager.Core.Models;
using FileManager.Core.Providers;

namespace FileManager.App.Tests.Fakes;

public sealed class TestableViewModel : ViewModelBase
{
    public TestableViewModel(IStorageProvider provider, StoragePath startingFolder, INotificationService notifications)
        : base(provider, startingFolder, "Test", notifications)
    {
    }
}