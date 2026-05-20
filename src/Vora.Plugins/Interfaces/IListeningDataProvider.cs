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
