namespace RoadWatcher.Core;

/// <summary>
/// Retrieves advisory road-context features for a bounded, explicitly requested GPX corridor.
/// Implementations must not be called from the playback path.
/// </summary>
public interface IRoadContextProvider
{
    string Provider { get; }

    Task<RoadContextProviderResult> FetchAsync(
        RoadContextQuery query,
        CancellationToken cancellationToken = default);
}
