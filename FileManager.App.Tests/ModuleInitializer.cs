using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace FileManager.App.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Ensures ReactiveUI and Splat dependency resolution are bound 
        // before any test fixture or static field initializes ReactiveObjects.
        RxAppBuilder.CreateReactiveUIBuilder().BuildApp();
    }
}