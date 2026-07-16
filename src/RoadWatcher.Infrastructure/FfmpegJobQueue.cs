using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using RoadWatcher.Core;
using Serilog;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// A single bounded FFmpeg scheduler for the application. Work items are
/// intentionally independent of UI controls, which makes cancellation safe
/// even when a flyout or preview is closed while encoding is in progress.
/// </summary>
public sealed class FfmpegJobQueue : IFfmpegJobQueue
{
    public const int DefaultConcurrency = 2;

    private readonly object _gate = new();
    private readonly Channel<QueuedJob> _pending = Channel.CreateUnbounded<QueuedJob>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });
    private readonly Dictionary<Guid, QueuedJob> _jobs = [];
    private readonly Func<string> _executableResolver;
    private readonly Task[] _workers;
    private bool _disposed;

    public FfmpegJobQueue(int maximumConcurrency = DefaultConcurrency, Func<string>? executableResolver = null)
    {
        if (maximumConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        _executableResolver = executableResolver ??
            (() => Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg");
        _workers = Enumerable.Range(0, maximumConcurrency)
            .Select(_ => Task.Run(WorkerAsync))
            .ToArray();
    }

    public event EventHandler? JobsChanged;

    public IReadOnlyList<FfmpegJobSnapshot> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Values
                    .OrderByDescending(job => job.EnqueuedAt)
                    .Select(job => job.ToSnapshot())
                    .ToArray();
            }
        }
    }

    public Task<FfmpegJobResult> EnqueueAsync(
        FfmpegJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var job = new QueuedJob(request, cancellationToken);
        lock (_gate)
        {
            _jobs.Add(job.Id, job);
        }
        RaiseJobsChanged();

        if (!_pending.Writer.TryWrite(job))
        {
            Complete(job, FfmpegJobState.Failed, "The FFmpeg queue is no longer accepting work.");
        }

        return job.Completion.Task;
    }

    public void Cancel(Guid jobId)
    {
        QueuedJob? job;
        lock (_gate)
        {
            _jobs.TryGetValue(jobId, out job);
        }
        job?.Cancellation.Cancel();
    }

    public void CancelAll()
    {
        QueuedJob[] jobs;
        lock (_gate)
        {
            jobs = _jobs.Values
                .Where(job => job.State is FfmpegJobState.Queued or FfmpegJobState.Running)
                .ToArray();
        }
        foreach (var job in jobs)
        {
            job.Cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAll();
        _pending.Writer.TryComplete();
    }

    private async Task WorkerAsync()
    {
        await foreach (var job in _pending.Reader.ReadAllAsync())
        {
            if (job.Cancellation.IsCancellationRequested)
            {
                Cancelled(job);
                continue;
            }

            lock (_gate)
            {
                job.State = FfmpegJobState.Running;
                job.StartedAt = DateTimeOffset.UtcNow;
            }
            RaiseJobsChanged();

            try
            {
                var result = await RunProcessAsync(job);
                if (result.ExitCode != 0)
                {
                    Log.Warning(
                        "FFmpeg job {Operation} for {SourceName} exited with {ExitCode}: {StandardError}",
                        job.Request.Operation,
                        Path.GetFileName(job.Request.SourcePath),
                        result.ExitCode,
                        SanitizeError(job.Request, result.StandardError));
                    throw new InvalidOperationException(
                        $"FFmpeg exited with code {result.ExitCode}: {SanitizeError(job.Request, result.StandardError)}");
                }

                lock (_gate)
                {
                    job.State = FfmpegJobState.Completed;
                    job.Progress = 1;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }
                job.Completion.TrySetResult(result);
                RaiseJobsChanged();
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                Cancelled(job);
            }
            catch (Exception exception)
            {
                Complete(job, FfmpegJobState.Failed, exception.Message);
                job.Completion.TrySetException(exception);
            }
        }
    }

    private async Task<FfmpegJobResult> RunProcessAsync(QueuedJob job)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = job.Request.ExecutablePath ?? _executableResolver(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add("-nostdin");
        foreach (var argument in job.Request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var progressTask = ReadProgressAsync(process, job);
        try
        {
            await process.WaitForExitAsync(job.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        await progressTask;
        return new FfmpegJobResult(process.ExitCode, await standardErrorTask);
    }

    private async Task ReadProgressAsync(Process process, QueuedJob job)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            if ((key is "out_time_us" or "out_time_ms") &&
                long.TryParse(value, out var microseconds) &&
                job.Request.SourceDuration is { } duration && duration > TimeSpan.Zero)
            {
                var progress = Math.Clamp(microseconds / (duration.TotalMilliseconds * 1000d), 0, .995);
                lock (_gate)
                {
                    if (job.State == FfmpegJobState.Running)
                    {
                        job.Progress = progress;
                    }
                }
                RaiseJobsChanged();
            }
        }
    }

    private void Cancelled(QueuedJob job)
    {
        Complete(job, FfmpegJobState.Cancelled, "Cancelled by the user.");
        job.Completion.TrySetException(new FfmpegJobCanceledException("FFmpeg job was cancelled."));
    }

    private void Complete(QueuedJob job, FfmpegJobState state, string? error)
    {
        lock (_gate)
        {
            job.State = state;
            job.Error = error;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        RaiseJobsChanged();
    }

    private static string SanitizeError(FfmpegJobRequest request, string error)
    {
        var sanitized = error
            .Replace(request.SourcePath, Path.GetFileName(request.SourcePath), StringComparison.OrdinalIgnoreCase)
            .Replace(request.DestinationPath, Path.GetFileName(request.DestinationPath), StringComparison.OrdinalIgnoreCase)
            .Trim();
        return sanitized.Length <= 1_000 ? sanitized : sanitized[..1_000];
    }

    private void RaiseJobsChanged() => JobsChanged?.Invoke(this, EventArgs.Empty);

    private sealed class QueuedJob
    {
        public QueuedJob(FfmpegJobRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            EnqueuedAt = DateTimeOffset.UtcNow;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public FfmpegJobRequest Request { get; }
        public CancellationTokenSource Cancellation { get; }
        public TaskCompletionSource<FfmpegJobResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FfmpegJobState State { get; set; } = FfmpegJobState.Queued;
        public double? Progress { get; set; }
        public DateTimeOffset EnqueuedAt { get; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Error { get; set; }

        public FfmpegJobSnapshot ToSnapshot() => new(
            Id,
            Request.Operation,
            Request.DisplayName,
            Path.GetFileName(Request.SourcePath),
            Path.GetFileName(Request.DestinationPath),
            State,
            Progress,
            EnqueuedAt,
            StartedAt,
            CompletedAt,
            Error);
    }
}
