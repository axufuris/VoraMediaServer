using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class LocalMetadataProvider : IMetadataProvider
{
    public string Id => "local_metadata";
    public string Name => "Local Assets (NFO & Images)";
    public string Version => "1.0.0";
    public string Description => "Reads local .nfo files and image assets (poster.jpg, backdrop.jpg) directly from your media folders.";
    public bool IsSystemPlugin => true;
    public string Type => "Metadata";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow, LibraryKind.Music, LibraryKind.HomeVideo };

    public string ProviderName => "Local";

    private readonly ILogger<LocalMetadataProvider> _logger;

    public LocalMetadataProvider(ILogger<LocalMetadataProvider> logger)
    {
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ParseMovieDirectory(query), cancellationToken);
    }

    public async Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ParseMovieDirectory(id), cancellationToken);
    }

    public async Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ParseTvShowDirectory(query), cancellationToken);
    }

    public async Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ParseTvShowDirectory(id), cancellationToken);
    }

    public async Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ParseEpisode(showTmdbId, seasonNumber, episodeNumber), cancellationToken);
    }

    public Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ActorMetadataResult?>(null);
    }

    private MetadataResult? ParseMovieDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;

        var nfoFiles = SafeGetFiles(path, "*.nfo");
        var movieNfo = nfoFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("movie.nfo", StringComparison.OrdinalIgnoreCase))
                       ?? nfoFiles.FirstOrDefault(); // Fallback to any NFO in the folder

        var result = new MetadataResult();

        if (movieNfo != null)
        {
            try
            {
                var doc = XDocument.Load(movieNfo);
                var root = doc.Root;
                if (root != null)
                {
                    result.Title = root.Element("title")?.Value;
                    result.OriginalTitle = root.Element("originaltitle")?.Value;
                    result.Overview = root.Element("plot")?.Value;
                    result.Tagline = root.Element("tagline")?.Value;
                    result.ContentRating = root.Element("mpaa")?.Value;

                    if (DateOnly.TryParse(root.Element("premiered")?.Value ?? root.Element("releasedate")?.Value, out var date))
                        result.ReleaseDate = date;

                    if (decimal.TryParse(root.Element("rating")?.Value ?? root.Element("userrating")?.Value, out var rating))
                        result.Rating = rating;

                    if (int.TryParse(root.Element("runtime")?.Value, out var runtime))
                        result.RuntimeMinutes = runtime;

                    result.TmdbId = root.Element("tmdbid")?.Value;
                    result.ImdbId = root.Element("imdbid")?.Value;

                    foreach (var actorNode in root.Elements("actor"))
                    {
                        var name = actorNode.Element("name")?.Value;
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name,
                                CharacterName = actorNode.Element("role")?.Value,
                                ProfileImageUrl = actorNode.Element("thumb")?.Value,
                                Roles = CastRole.Actor
                            });
                        }
                    }

                    foreach (var directorNode in root.Elements("director"))
                    {
                        var name = directorNode.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name.Trim(),
                                Roles = CastRole.Director
                            });
                        }
                    }

                    foreach (var writerNode in root.Elements("credits"))
                    {
                        var name = writerNode.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name.Trim(),
                                Roles = CastRole.Writer
                            });
                        }
                    }

                    foreach (var tagNode in root.Elements("tag"))
                    {
                        var tagValue = tagNode.Value?.ToLower().Replace(" ", "").Replace("-", "");
                        if (!string.IsNullOrWhiteSpace(tagValue))
                        {
                            if (tagValue == "duringcreditsstinger" || tagValue == "midcreditsstinger")
                                result.HasMidCreditsStinger = true;

                            if (tagValue == "aftercreditsstinger" || tagValue == "postcreditsstinger")
                                result.HasPostCreditsStinger = true;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        var poster = SafeGetFiles(path, "poster.*").FirstOrDefault() ?? SafeGetFiles(path, "folder.*").FirstOrDefault();
        var backdrop = SafeGetFiles(path, "fanart.*").FirstOrDefault() ?? SafeGetFiles(path, "backdrop.*").FirstOrDefault();

        if (poster != null) result.PosterUrl = poster;
        if (backdrop != null) result.BackgroundUrl = backdrop;

        if (result.Title == null && result.PosterUrl == null) return null;

        return result;
    }

    private MetadataResult? ParseTvShowDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;

        var tvShowNfo = Path.Combine(path, "tvshow.nfo");
        var result = new MetadataResult();

        if (File.Exists(tvShowNfo))
        {
            try
            {
                var doc = XDocument.Load(tvShowNfo);
                var root = doc.Root;
                if (root != null)
                {
                    result.Title = root.Element("title")?.Value;
                    result.OriginalTitle = root.Element("originaltitle")?.Value;
                    result.Overview = root.Element("plot")?.Value;
                    result.ContentRating = root.Element("mpaa")?.Value;

                    if (DateOnly.TryParse(root.Element("premiered")?.Value, out var date))
                        result.ReleaseDate = date;

                    if (decimal.TryParse(root.Element("rating")?.Value, out var rating))
                        result.Rating = rating;

                    result.TmdbId = root.Element("tmdbid")?.Value;
                    result.ImdbId = root.Element("imdbid")?.Value;
                    result.Status = root.Element("status")?.Value;

                    foreach (var actorNode in root.Elements("actor"))
                    {
                        var name = actorNode.Element("name")?.Value;
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name,
                                CharacterName = actorNode.Element("role")?.Value,
                                ProfileImageUrl = actorNode.Element("thumb")?.Value,
                                Roles = CastRole.Actor
                            });
                        }
                    }

                    foreach (var directorNode in root.Elements("director"))
                    {
                        var name = directorNode.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name.Trim(),
                                Roles = CastRole.Director
                            });
                        }
                    }

                    foreach (var writerNode in root.Elements("credits"))
                    {
                        var name = writerNode.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Cast.Add(new CastMemberResult
                            {
                                Name = name.Trim(),
                                Roles = CastRole.Writer
                            });
                        }
                    }
                }
            }
            catch { }
        }

        var poster = SafeGetFiles(path, "poster.*").FirstOrDefault() ?? SafeGetFiles(path, "folder.*").FirstOrDefault();
        var backdrop = SafeGetFiles(path, "fanart.*").FirstOrDefault() ?? SafeGetFiles(path, "backdrop.*").FirstOrDefault();

        if (poster != null) result.PosterUrl = poster;
        if (backdrop != null) result.BackgroundUrl = backdrop;

        if (result.Title == null && result.PosterUrl == null) return null;

        return result;
    }

    private string? FindEpisodeNfo(string showPath, string token)
    {
        return ResilientDirectory.EnumerateFiles(
                showPath,
                (directory, ex) => _logger.LogWarning(ex, "Could not read {Directory} looking for episode NFOs; skipping it.", directory))
            .FirstOrDefault(file =>
                file.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(file).Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    // A media folder can stop being readable at any moment — it lives on a
    // network share. Throwing here aborted the whole metadata refresh for the
    // item, so an unreadable folder degrades to "no local assets" and is logged.
    private string[] SafeGetFiles(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Directory} looking for {Pattern}; treating it as having no local assets.", directory, pattern);
            return Array.Empty<string>();
        }
    }

    private MetadataResult? ParseEpisode(string showPath, int seasonNumber, int episodeNumber)
    {
        if (string.IsNullOrWhiteSpace(showPath) || !Directory.Exists(showPath)) return null;

        // Two walks rather than one, to keep the original precedence: an S01E01
        // file anywhere in the tree beats a 1x01 file.
        var nfoFile = FindEpisodeNfo(showPath, $"S{seasonNumber:D2}E{episodeNumber:D2}")
            ?? FindEpisodeNfo(showPath, $"{seasonNumber}x{episodeNumber:D2}");

        if (nfoFile == null) return null;

        var result = new MetadataResult();
        try
        {
            var doc = XDocument.Load(nfoFile);
            var root = doc.Root;
            if (root != null)
            {
                result.Title = root.Element("title")?.Value;
                result.Overview = root.Element("plot")?.Value;

                if (DateOnly.TryParse(root.Element("aired")?.Value, out var date))
                    result.ReleaseDate = date;

                if (decimal.TryParse(root.Element("rating")?.Value, out var rating))
                    result.Rating = rating;

                if (int.TryParse(root.Element("runtime")?.Value, out var runtime))
                    result.RuntimeMinutes = runtime;
            }
        }
        catch { }

        var baseName = Path.GetFileNameWithoutExtension(nfoFile);
        var dir = Path.GetDirectoryName(nfoFile) ?? showPath;

        var thumb = SafeGetFiles(dir, $"{baseName}-thumb.*").FirstOrDefault() ??
                    SafeGetFiles(dir, $"{baseName}.jpg").FirstOrDefault() ??
                    SafeGetFiles(dir, $"{baseName}.png").FirstOrDefault();

        if (thumb != null)
        {
            result.PosterUrl = thumb;
            result.BackgroundUrl = thumb;
        }

        return result;
    }
}
