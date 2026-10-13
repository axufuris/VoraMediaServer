using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class LocalArtworkProvider(ILogger<LocalArtworkProvider> logger) : IArtworkProvider
{
    public const string ProviderId = "local_artwork";

    public string Id => ProviderId;
    public string Name => "Local Assets Artwork";
    public string Version => "1.1.0";
    public string Description => "Finds posters, backdrops and logos located in your local media folders.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => [LibraryKind.Movie, LibraryKind.TvShow, LibraryKind.Music, LibraryKind.HomeVideo];
    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

    public Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<ArtworkResult> results = string.IsNullOrWhiteSpace(localPath)
            ? []
            : LocalMediaAssets.FindArtwork(mediaType, localPath, null, new LocalMediaFolders(logger));
        return Task.FromResult(results);
    }
}
