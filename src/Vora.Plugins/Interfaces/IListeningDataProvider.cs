namespace Vora.Plugins.Interfaces;

public interface IListeningDataProvider : IVoraPlugin
{
    Task<string?> GetAuthTokenAsync(CancellationToken cancellationToken);

    Task<string> BuildAuthUrlAsync(string token, CancellationToken cancellationToken);

    Task<ListeningSession?> ExchangeTokenForSessionAsync(string token, CancellationToken cancellationToken);

    Task<bool> ScrobbleAsync(string sessionKey, string artist, string track, string? album, DateTime playedAt, int? durationSeconds, CancellationToken cancellationToken);

    Task<bool> UpdateNowPlayingAsync(string sessionKey, string artist, string track, string? album, int? durationSeconds, CancellationToken cancellationToken);

    Task<IReadOnlyList<SimilarArtistResult>> GetSimilarArtistsAsync(string artistName, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArtistTagResult>> GetArtistTopTagsAsync(string artistName, int limit, CancellationToken cancellationToken);

    // The artist's world-wide popularity plus its top tracks and albums, in as
    // few calls as the provider allows. Per-track lookups would cost one call per
    // track in the library; a top-tracks list covers an artist in one.
    Task<ArtistPopularity> GetArtistPopularityAsync(string artistName, int trackLimit, int albumLimit, CancellationToken cancellationToken);
}

// Three outcomes, because the caller does something different with each and
// the call budget depends on telling them apart.
public enum PopularityLookupOutcome
{
    // Data came back; store it and mark the artist refreshed.
    Found = 0,

    // The provider answered and does not know this artist. Mark it refreshed
    // anyway, or it is asked about again on every run for nothing.
    NotFound = 1,

    // No answer at all: not configured, offline, rate-limited, erroring. Leave
    // the artist unrefreshed so it is retried, and stop the batch rather than
    // spend the rest of it on a service that is not answering.
    Unavailable = 2,
}

public sealed class ArtistPopularity
{
    public PopularityLookupOutcome Outcome { get; init; }
    public long? Listeners { get; init; }
    public long? Plays { get; init; }
    public IReadOnlyList<NamedPopularity> TopTracks { get; init; } = Array.Empty<NamedPopularity>();
    public IReadOnlyList<NamedPopularity> TopAlbums { get; init; } = Array.Empty<NamedPopularity>();

    public static ArtistPopularity NotFound { get; } = new() { Outcome = PopularityLookupOutcome.NotFound };
    public static ArtistPopularity Unavailable { get; } = new() { Outcome = PopularityLookupOutcome.Unavailable };
}

public sealed class NamedPopularity
{
    public required string Name { get; init; }
    public long? Listeners { get; init; }
    public long? Plays { get; init; }
}

public sealed class ListeningSession
{
    public required string SessionKey { get; init; }
    public required string Username { get; init; }
}

public sealed class SimilarArtistResult
{
    public required string Name { get; init; }
    public double Score { get; init; }
}

public sealed class ArtistTagResult
{
    public required string Tag { get; init; }
    public int Weight { get; init; }
}
