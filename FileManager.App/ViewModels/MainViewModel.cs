using FileManager.Infrastructure.Providers;
using FileManager.Core.Providers;
using ReactiveUI;

namespace FileManager.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    public MainViewModel(IStorageProvider provider) : base(provider) { }
}
