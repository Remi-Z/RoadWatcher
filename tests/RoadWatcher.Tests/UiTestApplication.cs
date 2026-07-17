using Avalonia;
using Avalonia.Headless;
using RoadWatcherApp = RoadWatcher.App.App;

[assembly: AvaloniaTestApplication(typeof(RoadWatcher.Tests.UiTestApplication))]

namespace RoadWatcher.Tests;

/// <summary>
/// Enables deterministic, off-screen rendering of the real Avalonia application
/// for UI audit tests. It intentionally uses the app's actual theme resources.
/// </summary>
public static class UiTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<RoadWatcherApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false
        });
}
