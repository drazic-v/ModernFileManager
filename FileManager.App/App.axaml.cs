using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileManager.App.ViewModels;
using FileManager.App.Views;
using FileManager.Infrastructure.Providers;

namespace FileManager.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var provider = new LocalStorageProvider();
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(provider) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}