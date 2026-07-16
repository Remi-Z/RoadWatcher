using Avalonia;
using Serilog;

namespace RoadWatcher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RoadWatcherRuntime.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled app-domain exception");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "Unobserved background task exception");
            eventArgs.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "RoadWatcher terminated during startup or UI dispatch");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
