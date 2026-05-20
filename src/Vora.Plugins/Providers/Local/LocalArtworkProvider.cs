using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class LocalArtworkProvider : IArtworkProvider
{
    public string Id => "local_artwork";
    public string Name => "Local Assets Artwork";
    public string Version => "1.0.0";
    public string Description => "Finds posters and backdrops located in your local media folders.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow", "Music", "HomeVideo" };

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null)
    {
        var results = new List<ArtworkResult>();

        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            return Task.FromResult<IEnumerable<ArtworkResult>>(results);

        var files = Directory.GetFiles(localPath);

        var posterFiles = files.Where(f =>
            Path.GetFileNameWithoutExtension(f).Equals("poster", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(f).Equals("folder", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(f).Equals("cover", StringComparison.OrdinalIgnoreCase));

        foreach (var file in posterFiles)
        {
            results.Add(new ArtworkResult { Kind = ArtworkKind.Poster, Url = file, Language = "Local" });
        }

        var backdropFiles = files.Where(f =>
            Path.GetFileNameWithoutExtension(f).Equals("fanart", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(f).Equals("backdrop", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(f).Equals("background", StringComparison.OrdinalIgnoreCase));

        foreach (var file in backdropFiles)
        {
            results.Add(new ArtworkResult { Kind = ArtworkKind.Backdrop, Url = file, Language = "Local" });
        }

        return Task.FromResult<IEnumerable<ArtworkResult>>(results);
    }
}
