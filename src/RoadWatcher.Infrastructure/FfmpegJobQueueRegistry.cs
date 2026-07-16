using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Compatibility path for services constructed outside the composition root.
/// The desktop workbench supplies its own instance, while this fallback still
/// prevents independent generators from escaping the two-process ceiling.
/// </summary>
internal static class FfmpegJobQueueRegistry
{
    private static readonly Lazy<IFfmpegJobQueue> SharedQueue = new(() => new FfmpegJobQueue());

    public static IFfmpegJobQueue Shared => SharedQueue.Value;
}
