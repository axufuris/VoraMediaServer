using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMetadataProvider : IVoraPlugin
{
    string ProviderName { get; }

    Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null);
    Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null);
    Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source);
    Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source);
    Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber);
    Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId);
}
