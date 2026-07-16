using System.Reflection;
using System.Text.Json;
using Serilog;
using Serilog.Events;

namespace RoadWatcher.App;

public enum RoadWatcherLogLevel
{
    Error,
    Warning,
    Information
}

/// <summary>
/// App-local preferences. This model is deliberately outside ProjectDocument:
/// workstation configuration never becomes reviewer evidence or export data.
/// </summary>
public sealed record RoadWatcherSettings
{
    public RoadWatcherLogLevel LogLevel { get; init; } = RoadWatcherLogLevel.Warning;
    public bool UseManualFfmpegPath { get; init; }
    public string? FfmpegExecutablePath { get; init; }
    public ContextMapStyle PreferredMapStyle { get; init; } = ContextMapStyle.Night;
}

public sealed class RoadWatcherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RoadWatcher",
        "settings.json");

    public RoadWatcherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new RoadWatcherSettings();
            }

            return JsonSerializer.Deserialize<RoadWatcherSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new RoadWatcherSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Could not load local RoadWatcher settings; using defaults");
            return new RoadWatcherSettings();
        }
    }

    public void Save(RoadWatcherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("Settings directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(SettingsPath)}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

public static class RoadWatcherRuntime
{
    private static readonly RoadWatcherSettingsStore Store = new();
    private static bool _initialized;

    public static RoadWatcherSettings Settings { get; private set; } = new();
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RoadWatcher",
        "logs");
    public static string LogPathHint => Path.Combine(LogDirectory, "roadwatcher-YYYYMMDD.log");
    public static string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development";

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        Settings = Store.Load();
        RoadWatcherLogging.Configure(Settings.LogLevel);
        _initialized = true;
    }

    public static void SaveSettings(RoadWatcherSettings settings)
    {
        Settings = settings;
        Store.Save(settings);
        RoadWatcherLogging.Configure(settings.LogLevel);
    }
}

public static class RoadWatcherLogging
{
    public static void Configure(RoadWatcherLogLevel level)
    {
        Directory.CreateDirectory(RoadWatcherRuntime.LogDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level switch
            {
                RoadWatcherLogLevel.Error => LogEventLevel.Error,
                RoadWatcherLogLevel.Information => LogEventLevel.Information,
                _ => LogEventLevel.Warning
            })
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(RoadWatcherRuntime.LogDirectory, "roadwatcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }
}
