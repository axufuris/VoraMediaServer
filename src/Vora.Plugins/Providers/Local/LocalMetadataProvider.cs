using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class LocalMetadataProvider(ILogger<LocalMetadataProvider> logger) : IMetadataProvider
{
    public const string ProviderId = "local_metadata";
    public const string LocalSource = "local";
    public const string LocalProviderName = "Local";

    public string Id => ProviderId;
    public string Name => "Local Assets (NFO & Images)";
    public string Version => "1.1.0";
    public string Description => "Reads Emby, Jellyfin and Kodi NFO metadata directly from your media folders.";
    public bool IsSystemPlugin => true;
    public string Type => "Metadata";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => [LibraryKind.Movie, LibraryKind.TvShow, LibraryKind.Music, LibraryKind.HomeVideo];
    public string ProviderName => LocalProviderName;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

    public Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => LocalNfoReader.ReadMovie(query, new LocalMediaFolders(logger)), cancellationToken);

    public Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default) =>
        FetchMovieMetadataAsync(id, cancellationToken: cancellationToken);

    public Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => LocalNfoReader.ReadTvShow(query, new LocalMediaFolders(logger)), cancellationToken);

    public Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default) =>
        FetchTvShowMetadataAsync(id, cancellationToken: cancellationToken);

    public Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default) =>
        Task.Run(() => LocalNfoReader.ReadEpisode(showTmdbId, seasonNumber, episodeNumber, new LocalMediaFolders(logger)), cancellationToken);

    public Task<MetadataResult?> FetchSeasonMetadataAsync(string showId, string source, int seasonNumber, CancellationToken cancellationToken = default) =>
        Task.Run(() => LocalNfoReader.ReadSeason(showId, seasonNumber, new LocalMediaFolders(logger)), cancellationToken);

    public Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActorMetadataResult?>(null);
}
