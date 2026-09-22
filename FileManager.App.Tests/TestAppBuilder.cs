using Avalonia;
using Avalonia.Headless;
using FileManager.App.Tests;
using ReactiveUI.Builder;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace FileManager.App.Tests;

public static class TestAppBuilder
{
    private static bool _reactiveUiInitialized;

    public static AppBuilder BuildAvaloniaApp()
    {
        if (!_reactiveUiInitialized)
        {
            RxAppBuilder.CreateReactiveUIBuilder().BuildApp();
            _reactiveUiInitialized = true;
        }

        return AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}