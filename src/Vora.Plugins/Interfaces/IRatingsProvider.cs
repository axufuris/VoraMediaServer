namespace Vora.Plugins.Interfaces;

public interface IRatingsProvider : IVoraPlugin
{
    string RatingSourceName { get; }
    Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType, CancellationToken cancellationToken = default);
}
