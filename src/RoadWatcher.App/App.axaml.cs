using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace RoadWatcher.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyThemeMode(RoadWatcherRuntime.Settings.ThemeMode);
    }

    public void ApplyThemeMode(ApplicationThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ApplicationThemeMode.Light => ThemeVariant.Light,
            ApplicationThemeMode.System => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var projectDirectory = desktop.Args?
                .FirstOrDefault(argument => argument.EndsWith(".roadwatcher", StringComparison.OrdinalIgnoreCase));
            desktop.MainWindow = new MainWindow(projectDirectory);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
