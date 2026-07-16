using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Optional, machine-readable media metadata reader. ffprobe is deliberately
/// advisory: import still works when it is absent and LibVLC remains the
/// fallback for basic probing.
/// </summary>
public sealed partial class FfprobeMediaMetadataReader : IMediaMetadataReader
{
    private readonly string? _configuredExecutable;

    public FfprobeMediaMetadataReader(string? configuredExecutable = null)
    {
        _configuredExecutable = configuredExecutable;
    }

    public async Task<MediaCaptureMetadata?> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-v", "error",
                     "-print_format", "json",
                     "-show_entries",
                     "format_tags=creation_time,com.apple.quicktime.creationdate:stream=codec_name,codec_type,width,height,avg_frame_rate,r_frame_rate:stream_tags=creation_time,com.apple.quicktime.creationdate",
                     "-show_format",
                     "-show_streams",
                     Path.GetFullPath(path)
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(probeCancellation.Token);
            var errorTask = process.StandardError.ReadToEndAsync(probeCancellation.Token);
            await process.WaitForExitAsync(probeCancellation.Token);
            var output = await outputTask;
            _ = await errorTask;
            return process.ExitCode == 0
                ? Parse(output, new FileInfo(path).LastWriteTimeUtc)
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Terminate(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Metadata is enrichment only: a hung local ffprobe must not
            // prevent LibVLC from importing playable evidence.
            Terminate(process);
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static MediaCaptureMetadata Parse(string json, DateTimeOffset? fileSystemRecordedAtHint = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.TryGetProperty("format", out var formatElement)
            ? formatElement
            : default;
        var formatTags = GetTags(format);
        var video = GetVideoStream(root);
        var videoTags = GetTags(video);
        var candidates = new (string? Raw, MediaCaptureTimestampSource Source)[]
        {
            (GetString(formatTags, "com.apple.quicktime.creationdate"), MediaCaptureTimestampSource.QuickTimeCreationDate),
            (GetString(videoTags, "com.apple.quicktime.creationdate"), MediaCaptureTimestampSource.QuickTimeCreationDate),
            (GetString(formatTags, "creation_time"), MediaCaptureTimestampSource.ContainerCreationTime),
            (GetString(videoTags, "creation_time"), MediaCaptureTimestampSource.VideoStreamCreationTime)
        };
        DateTimeOffset? timestamp = null;
        string? rawTimestamp = null;
        var source = MediaCaptureTimestampSource.Unknown;
        var hasExplicitOffset = false;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Raw))
            {
                continue;
            }

            rawTimestamp ??= candidate.Raw;
            if (source == MediaCaptureTimestampSource.Unknown)
            {
                source = candidate.Source;
            }

            if (!TryParseTimestamp(candidate.Raw, out var capturedAt, out var parsedWithExplicitOffset))
            {
                continue;
            }

            timestamp = capturedAt;
            rawTimestamp = candidate.Raw;
            source = candidate.Source;
            hasExplicitOffset = parsedWithExplicitOffset;
            break;
        }
        var confidence = timestamp is null
            ? MediaCaptureTimestampConfidence.Unknown
            : hasExplicitOffset
                ? MediaCaptureTimestampConfidence.Trusted
                : MediaCaptureTimestampConfidence.AssumedLocal;
        return new MediaCaptureMetadata(
            timestamp,
            rawTimestamp,
            source,
            hasExplicitOffset,
            confidence,
            fileSystemRecordedAtHint,
            GetString(video, "codec_name"),
            GetInt32(video, "width"),
            GetInt32(video, "height"),
            ParseFrameRate(GetString(video, "avg_frame_rate")) ?? ParseFrameRate(GetString(video, "r_frame_rate")));
    }

    private string ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_configuredExecutable))
        {
            return _configuredExecutable;
        }

        var explicitFfprobe = Environment.GetEnvironmentVariable("ROADWATCHER_FFPROBE");
        if (!string.IsNullOrWhiteSpace(explicitFfprobe))
        {
            return explicitFfprobe;
        }

        var ffmpeg = Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG");
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(ffmpeg));
            var extension = Path.GetExtension(ffmpeg);
            var sibling = Path.Combine(directory ?? string.Empty, $"ffprobe{extension}");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        return "ffprobe";
    }

    private static JsonElement GetVideoStream(JsonElement root)
    {
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (var stream in streams.EnumerateArray())
        {
            if (string.Equals(GetString(stream, "codec_type"), "video", StringComparison.OrdinalIgnoreCase))
            {
                return stream;
            }
        }

        return default;
    }

    private static JsonElement GetTags(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("tags", out var tags)
            ? tags
            : default;

    private static string? GetString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static int? GetInt32(JsonElement value, string propertyName)
    {
        var text = GetString(value, propertyName);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0")
        {
            return null;
        }

        var parts = value.Split('/', 2);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)
            ? fps
            : null;
    }

    private static bool TryParseTimestamp(string? raw, out DateTimeOffset timestamp, out bool hasExplicitOffset)
    {
        timestamp = default;
        hasExplicitOffset = false;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        hasExplicitOffset = ExplicitOffsetPattern().IsMatch(trimmed);
        if (hasExplicitOffset)
        {
            return DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out timestamp);
        }

        if (!DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localDateTime))
        {
            return false;
        }

        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        timestamp = new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
        return true;
    }

    private static void Terminate(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and termination.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Import remains available even when Windows refuses termination.
        }
    }

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetPattern();
}
