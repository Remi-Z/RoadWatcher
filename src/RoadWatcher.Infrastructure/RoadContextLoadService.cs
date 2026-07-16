using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Aggregates independently queried advisory providers into one immutable, reviewer-requested snapshot.
/// It intentionally has no playback subscription or timer: callers invoke it only for an explicit
/// load/refresh action, then playback reads the persisted/cached snapshot without HTTP.
/// </summary>
public sealed class RoadContextLoadService
{
    private readonly IReadOnlyList<IRoadContextProvider> _providers;

    public RoadContextLoadService(IEnumerable<IRoadContextProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        if (_providers.Any(provider => provider is null))
        {
            throw new ArgumentException("Road-context providers cannot contain null entries.", nameof(providers));
        }
    }

    public async Task<RoadContextSnapshot> LoadAsync(
        RoadContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var fetchedAt = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(_providers
            .Select(provider => FetchSafelyAsync(provider, query, cancellationToken)));
        var corridorResults = results
            .Select(result => FilterToRequestedCorridor(result, query))
            .ToArray();

        // Keep every provider record. Deliberately do not collapse duplicates or decide which
        // source is authoritative; the snapshot must retain provenance for later UI review.
        return RoadContextSnapshot.Create(
            query,
            fetchedAt,
            corridorResults.SelectMany(result => result.Features),
            corridorResults.Select(result => result.Report));
    }

    private static async Task<RoadContextProviderResult> FetchSafelyAsync(
        IRoadContextProvider provider,
        RoadContextQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.FetchAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is control flow, not a failed provider request.
            throw;
        }
        catch (Exception exception)
        {
            return new RoadContextProviderResult(
                [],
                new RoadContextProviderReport(
                    string.IsNullOrWhiteSpace(provider.Provider) ? provider.GetType().Name : provider.Provider,
                    RoadContextProviderStatus.Failed,
                    DateTimeOffset.UtcNow,
                    0,
                    $"Provider failed without returning a report: {exception.Message}"));
        }
    }

    private static RoadContextProviderResult FilterToRequestedCorridor(
        RoadContextProviderResult result,
        RoadContextQuery query)
    {
        var retained = result.Features
            .Where(feature => RoadContextProviderSupport.IsWithinRouteCorridor(feature.Geometry, query))
            .ToArray();
        var omittedCount = result.Features.Count - retained.Length;
        if (omittedCount == 0 && result.Report.FeatureCount == retained.Length)
        {
            return result;
        }

        var filterMessage = omittedCount == 0
            ? null
            : $"{omittedCount} feature(s) outside the requested GPX corridor were omitted.";
        var message = string.Join(
            " ",
            new[] { result.Report.Message, filterMessage }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new RoadContextProviderResult(
            retained,
            result.Report with
            {
                FeatureCount = retained.Length,
                Message = string.IsNullOrWhiteSpace(message) ? null : message
            });
    }
}
