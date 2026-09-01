using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileManager.App.ViewModels;
using FileManager.App.Views;
using FileManager.Core.Models;
using FileManager.Infrastructure.Providers;
using System;

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
            var home =  Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
            var startingFolder = new StoragePath { ProviderId = provider.ProviderId, Value = home };
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(provider, startingFolder) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}