using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMetadataProvider : IVoraPlugin
{
    Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default);
    Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default);
    Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default);
    Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default);
    Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default);
    Task<MetadataResult?> FetchSeasonMetadataAsync(string showId, string source, int seasonNumber, CancellationToken cancellationToken = default)
        => Task.FromResult<MetadataResult?>(null);
    Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default);
}
