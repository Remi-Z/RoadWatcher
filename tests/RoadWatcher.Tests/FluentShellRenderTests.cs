using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using RoadWatcher.App;
using RoadWatcherApp = RoadWatcher.App.App;

namespace RoadWatcher.Tests;

public sealed class FluentShellRenderTests
{
    [AvaloniaFact]
    public void EmptyWorkbench_RendersTheActualFluentShellAtTheTargetViewport()
    {
        var window = CreateDarkWorkbench();
        try
        {
            var mapEmptyState = window.FindControl<Border>("MapEmptyState");
            var timelineEmptyState = window.FindControl<Border>("TimelineEmptyState");

            Assert.NotNull(mapEmptyState);
            Assert.True(mapEmptyState.IsVisible);
            Assert.NotNull(timelineEmptyState);
            Assert.True(timelineEmptyState.IsVisible);

            AssertWorkbenchSurface(window, "fluent-shell-empty.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EmptyJobsDrawer_RendersAnIntentionalFluentEmptyState()
    {
        var window = CreateDarkWorkbench(viewModel => viewModel.IsJobsDrawerOpen = true);
        try
        {
            var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);

            var jobsDrawer = window.FindControl<Border>("JobsDrawer");
            Assert.NotNull(jobsDrawer);
            Assert.True(jobsDrawer.IsVisible);
            Assert.True(viewModel.HasNoFfmpegJobs);

            AssertWorkbenchSurface(window, "fluent-shell-jobs-empty.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsOverlay_RendersOverTheFluentWorkbench()
    {
        var window = CreateDarkWorkbench(viewModel => viewModel.IsSettingsPageOpen = true);
        try
        {
            var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);

            var settingsOverlay = window.FindControl<Border>("SettingsOverlay");
            Assert.NotNull(settingsOverlay);
            Assert.True(settingsOverlay.IsVisible);

            AssertWorkbenchSurface(window, "fluent-shell-settings.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateDarkWorkbench(Action<MainWindowViewModel>? prepare = null)
    {
        RoadWatcherRuntime.Initialize();
        var app = Assert.IsType<RoadWatcherApp>(Application.Current);
        app.ApplyThemeMode(ApplicationThemeMode.Dark);

        var window = new MainWindow();
        prepare?.Invoke(Assert.IsType<MainWindowViewModel>(window.DataContext));
        window.Show();
        return window;
    }

    private static void AssertWorkbenchSurface(MainWindow window, string captureFileName)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(1152, frame.PixelSize.Width);
        Assert.Equal(820, frame.PixelSize.Height);

        var workbench = window.FindControl<Grid>("WorkbenchGrid");
        Assert.NotNull(workbench);
        Assert.True(workbench.Bounds.Width >= 1000);
        Assert.True(workbench.Bounds.Height >= 700);
        Assert.NotNull(window.FindControl<Control>("VideoPane"));
        Assert.NotNull(window.FindControl<Control>("MapPane"));
        Assert.NotNull(window.FindControl<Control>("InspectorPane"));
        Assert.NotNull(window.FindControl<Control>("TimelinePane"));

        if (Environment.GetEnvironmentVariable("ROADWATCHER_CAPTURE_UI") == "1")
        {
            frame.Save(Path.Combine(
                FindRepositoryRoot(),
                "docs",
                "milestones",
                "M11-fluent-workbench",
                captureFileName));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoadWatcher.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the RoadWatcher repository root.");
    }
}
