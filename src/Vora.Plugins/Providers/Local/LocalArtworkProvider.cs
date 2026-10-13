using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class LocalArtworkProvider(ILogger<LocalArtworkProvider>? logger = null) : IArtworkProvider
{
    public string Id => "local_artwork";
    public string Name => "Local Assets Artwork";
    public string Version => "1.0.0";
    public string Description => "Finds posters, backdrops and logos located in your local media folders.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => [LibraryKind.Movie, LibraryKind.TvShow, LibraryKind.Music, LibraryKind.HomeVideo];
    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

    public Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<ArtworkResult>();
        if (string.IsNullOrWhiteSpace(localPath)) return Task.FromResult<IEnumerable<ArtworkResult>>(results);

        var files = LocalMediaAssets.Files(LocalMediaAssets.DirectoryPath(localPath), logger);
        var baseName = Directory.Exists(localPath) ? null : Path.GetFileNameWithoutExtension(localPath);
        if (mediaType == "Episode")
        {
            if (baseName != null) Add(ArtworkKind.Backdrop, baseName + "-thumb", baseName);
        }
        else
        {
            Add(ArtworkKind.Poster, baseName == null ? "poster" : baseName + "-poster", "poster", "folder", "cover");
            Add(ArtworkKind.Backdrop, baseName == null ? "fanart" : baseName + "-fanart", "fanart", "backdrop", "background");
            if (mediaType != "Season") Add(ArtworkKind.Logo, baseName == null ? "logo" : baseName + "-logo", "logo", "clearlogo");
        }
        return Task.FromResult<IEnumerable<ArtworkResult>>(results);

        void Add(ArtworkKind kind, params string[] names)
        {
            var image = LocalMediaAssets.Image(files, names);
            if (image != null) results.Add(new ArtworkResult { Kind = kind, Url = image, Language = "Local" });
        }
    }
}
