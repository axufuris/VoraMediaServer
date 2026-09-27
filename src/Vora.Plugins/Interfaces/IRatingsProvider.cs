namespace Vora.Plugins.Interfaces;

public interface IRatingsProvider : IVoraPlugin
{
    string RatingSourceName { get; }

    // False when the provider can't currently be consulted (e.g. a rate-limit
    // circuit breaker is open). A rating slot backed by an unavailable provider
    // is left untouched during enrichment instead of being cleared, so a
    // temporary outage never wipes ratings fetched on an earlier run.
    bool IsCurrentlyAvailable => true;

    Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType, CancellationToken cancellationToken = default);
}
