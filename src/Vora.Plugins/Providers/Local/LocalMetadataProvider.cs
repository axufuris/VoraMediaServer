using System.Xml.Linq;
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

        var nfoFiles = Directory.GetFiles(path, "*.nfo");
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

                    if (DateTime.TryParse(root.Element("premiered")?.Value ?? root.Element("releasedate")?.Value, out var date))
                        result.ReleaseDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);

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

        var poster = Directory.GetFiles(path, "poster.*").FirstOrDefault() ?? Directory.GetFiles(path, "folder.*").FirstOrDefault();
        var backdrop = Directory.GetFiles(path, "fanart.*").FirstOrDefault() ?? Directory.GetFiles(path, "backdrop.*").FirstOrDefault();

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

                    if (DateTime.TryParse(root.Element("premiered")?.Value, out var date))
                        result.ReleaseDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);

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

        var poster = Directory.GetFiles(path, "poster.*").FirstOrDefault() ?? Directory.GetFiles(path, "folder.*").FirstOrDefault();
        var backdrop = Directory.GetFiles(path, "fanart.*").FirstOrDefault() ?? Directory.GetFiles(path, "backdrop.*").FirstOrDefault();

        if (poster != null) result.PosterUrl = poster;
        if (backdrop != null) result.BackgroundUrl = backdrop;

        if (result.Title == null && result.PosterUrl == null) return null;

        return result;
    }

    private MetadataResult? ParseEpisode(string showPath, int seasonNumber, int episodeNumber)
    {
        if (string.IsNullOrWhiteSpace(showPath) || !Directory.Exists(showPath)) return null;

        var searchPattern = $"*S{seasonNumber:D2}E{episodeNumber:D2}*.nfo";
        var nfoFile = Directory.GetFiles(showPath, searchPattern, SearchOption.AllDirectories).FirstOrDefault();

        if (nfoFile == null)
        {
            var altPattern = $"*{seasonNumber}x{episodeNumber:D2}*.nfo";
            nfoFile = Directory.GetFiles(showPath, altPattern, SearchOption.AllDirectories).FirstOrDefault();
        }

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

                if (DateTime.TryParse(root.Element("aired")?.Value, out var date))
                    result.ReleaseDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);

                if (decimal.TryParse(root.Element("rating")?.Value, out var rating))
                    result.Rating = rating;

                if (int.TryParse(root.Element("runtime")?.Value, out var runtime))
                    result.RuntimeMinutes = runtime;
            }
        }
        catch { }

        var baseName = Path.GetFileNameWithoutExtension(nfoFile);
        var dir = Path.GetDirectoryName(nfoFile) ?? showPath;

        var thumb = Directory.GetFiles(dir, $"{baseName}-thumb.*").FirstOrDefault() ??
                    Directory.GetFiles(dir, $"{baseName}.jpg").FirstOrDefault() ??
                    Directory.GetFiles(dir, $"{baseName}.png").FirstOrDefault();

        if (thumb != null)
        {
            result.PosterUrl = thumb;
            result.BackgroundUrl = thumb;
        }

        return result;
    }
}
