using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileManager.App.ViewModels;
using FileManager.App.Views;
using FileManager.Core.Models;
using FileManager.Infrastructure.Providers;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using System;
using System.Diagnostics;
using System.Reactive;

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
            RxAppBuilder.CreateReactiveUIBuilder()
            .WithExceptionHandler(Observer.Create<Exception>(ex =>
            {
                if (Debugger.IsAttached)
                    Debugger.Break();

                // Log or show a dialog
                Debug.WriteLine($"[Unhandled command exception]\n{ex}");
            }))
            .BuildApp();


            var provider = new LocalStorageProvider();
            var home =  Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
            var startingFolder = new StoragePath { ProviderId = provider.ProviderId, Value = home };
            desktop.MainWindow = new MainWindow { DataContext = new WorkspaceViewModel(provider, startingFolder, "Local") };
        }
        base.OnFrameworkInitializationCompleted();
    }
}