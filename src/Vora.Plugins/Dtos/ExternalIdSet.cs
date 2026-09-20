namespace Vora.Plugins.Dtos;

public sealed record ExternalIdSet
{
    public string? TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public string? TvdbId { get; init; }

    public static ExternalIdSet Empty { get; } = new();

    public bool IsEmpty => string.IsNullOrWhiteSpace(TmdbId)
        && string.IsNullOrWhiteSpace(ImdbId)
        && string.IsNullOrWhiteSpace(TvdbId);

    public static ExternalIdSet From(string? tmdbId = null, string? imdbId = null, string? tvdbId = null) =>
        new() { TmdbId = tmdbId, ImdbId = imdbId, TvdbId = tvdbId };
}
