using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace RoadWatcher.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
