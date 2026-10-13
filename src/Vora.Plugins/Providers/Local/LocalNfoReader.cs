using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Local;

public static class LocalNfoReader
{
    private static readonly Dictionary<string, int> Genres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = 28, ["Adventure"] = 12, ["Animation"] = 16, ["Comedy"] = 35,
        ["Crime"] = 80, ["Documentary"] = 99, ["Drama"] = 18, ["Family"] = 10751,
        ["Fantasy"] = 14, ["History"] = 36, ["Horror"] = 27, ["Music"] = 10402,
        ["Mystery"] = 9648, ["Romance"] = 10749, ["Science Fiction"] = 878,
        ["TV Movie"] = 10770, ["Thriller"] = 53, ["War"] = 10752, ["Western"] = 37,
        ["Action & Adventure"] = 10759, ["Kids"] = 10762, ["News"] = 10763,
        ["Reality"] = 10764, ["Sci-Fi & Fantasy"] = 10765, ["Soap"] = 10766,
        ["Talk"] = 10767, ["War & Politics"] = 10768
    };

    public static MetadataResult? ReadMovie(string path, ILogger logger)
    {
        var files = LocalMediaAssets.Files(LocalMediaAssets.DirectoryPath(path), logger);
        var candidates = new List<string?>();
        if (!Directory.Exists(path)) candidates.Add(LocalMediaAssets.NamedFile(files, Path.GetFileNameWithoutExtension(path) + ".nfo"));
        candidates.Add(LocalMediaAssets.NamedFile(files, "movie.nfo"));
        if (Directory.Exists(path)) candidates.AddRange(files.Where(file => Path.GetExtension(file).Equals(".nfo", StringComparison.OrdinalIgnoreCase)));
        return ReadFirst(candidates, "movie", logger);
    }

    public static MetadataResult? ReadTvShow(string path, ILogger logger) =>
        ReadFirst([LocalMediaAssets.NamedFile(LocalMediaAssets.Files(path, logger), "tvshow.nfo")], "tvshow", logger);

    public static MetadataResult? ReadSeason(string path, ILogger logger) =>
        ReadFirst([LocalMediaAssets.NamedFile(LocalMediaAssets.Files(path, logger), "season.nfo")], "season", logger);

    public static MetadataResult? ReadEpisode(string path, int seasonNumber, int episodeNumber, ILogger logger)
    {
        if (!Directory.Exists(path))
        {
            var files = LocalMediaAssets.Files(LocalMediaAssets.DirectoryPath(path), logger);
            return ReadFirst([LocalMediaAssets.NamedFile(files, Path.GetFileNameWithoutExtension(path) + ".nfo")], "episodedetails", logger);
        }

        var token = $"S{seasonNumber:D2}E{episodeNumber:D2}";
        var alternate = $"{seasonNumber}x{episodeNumber:D2}";
        var candidates = ResilientDirectory.EnumerateFiles(path,
                (directory, ex) => logger.LogWarning(ex, "Could not read episode NFOs in {Directory}.", directory))
            .Where(file => Path.GetExtension(file).Equals(".nfo", StringComparison.OrdinalIgnoreCase))
            .Where(file => ContainsEpisodeToken(Path.GetFileNameWithoutExtension(file), token)
                || ContainsEpisodeToken(Path.GetFileNameWithoutExtension(file), alternate))
            .OrderByDescending(file => ContainsEpisodeToken(Path.GetFileNameWithoutExtension(file), token))
            .ThenBy(file => file, StringComparer.Ordinal);
        return ReadFirst(candidates, "episodedetails", logger);
    }

    private static bool ContainsEpisodeToken(string name, string token)
    {
        var index = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && (index + token.Length == name.Length || !char.IsDigit(name[index + token.Length]));
    }

    private static MetadataResult? ReadFirst(IEnumerable<string?> candidates, string rootName, ILogger logger)
    {
        foreach (var path in candidates.OfType<string>().Distinct())
        {
            try
            {
                using var reader = XmlReader.Create(path, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4 * 1024 * 1024
                });
                var root = XDocument.Load(reader).Root;
                if (root == null || root.Name != rootName) continue;
                var result = Parse(root);
                if (result.Title != null || result.Overview != null || result.TmdbId != null || result.ImdbId != null
                    || result.TvdbId != null || result.ReleaseDate != null || result.Cast.Count > 0) return result;
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not import local metadata from {NfoPath}.", path);
            }
        }
        return null;
    }

    private static MetadataResult Parse(XElement root)
    {
        var result = new MetadataResult
        {
            Title = Text(root, "title"), OriginalTitle = Text(root, "originaltitle"),
            SortTitle = Text(root, "sorttitle"), Overview = Text(root, "plot"),
            Tagline = Text(root, "tagline"), ContentRating = Text(root, "mpaa"), Status = Text(root, "status"),
            TmdbId = ProviderId(root, "tmdb"), ImdbId = ProviderId(root, "imdb"), TvdbId = ProviderId(root, "tvdb")
        };
        foreach (var name in new[] { "premiered", "releasedate", "aired" })
        {
            if (DateOnly.TryParseExact(Text(root, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                result.ReleaseDate = date;
                break;
            }
        }
        if (result.ReleaseDate == null && int.TryParse(Text(root, "year"), out var year) && year is >= 1 and <= 9999)
            result.ReleaseDate = new DateOnly(year, 1, 1);
        if (int.TryParse(Text(root, "runtime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var runtime) && runtime > 0)
            result.RuntimeMinutes = runtime;
        if (decimal.TryParse(Text(root, "rating"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rating))
            result.Rating = rating;

        result.GenreIds = root.Elements("genre").Select(node => node.Value.Trim())
            .Where(Genres.ContainsKey).Select(name => Genres[name]).Distinct().ToList();

        foreach (var actor in root.Elements("actor"))
        {
            var name = Text(actor, "name");
            if (name != null) result.Cast.Add(new CastMemberResult
            {
                Name = name, CharacterName = Text(actor, "role"),
                TmdbId = PositiveId(Text(actor, "tmdbid")), TvdbId = PositiveId(Text(actor, "tvdbid")),
                ProfileImageUrl = ExistingImage(Text(actor, "thumb")), Roles = CastRole.Actor
            });
        }
        AddPeople(root, result, "director", CastRole.Director);
        AddPeople(root, result, "credits", CastRole.Writer);
        AddPeople(root, result, "writer", CastRole.Writer);

        foreach (var tag in root.Elements("tag"))
        {
            var value = tag.Value.ToLowerInvariant().Replace(" ", "").Replace("-", "");
            if (value is "duringcreditsstinger" or "midcreditsstinger") result.HasMidCreditsStinger = true;
            if (value is "aftercreditsstinger" or "postcreditsstinger") result.HasPostCreditsStinger = true;
        }
        var collectionName = Text(root.Element("set"), "name") ?? Text(root, "set");
        if (collectionName != null && int.TryParse(Text(root, "collectionnumber"), out var collectionId) && collectionId > 0)
            result.Collection = new CollectionResult { Id = collectionId, Name = collectionName };
        foreach (var trailer in root.Elements("trailer"))
        {
            var key = YoutubeKey(trailer.Value.Trim());
            if (key != null && !result.Videos.Any(video => video.Key == key))
                result.Videos.Add(new VideoResult { Key = key, Name = "Trailer", Site = "YouTube", Type = "Trailer" });
        }
        return result;
    }

    private static string? Text(XElement? root, string name)
    {
        var value = root?.Element(name)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ProviderId(XElement root, string provider) =>
        Text(root, provider + "id")
        ?? Text(root, provider + "_id")
        ?? root.Elements("uniqueid").FirstOrDefault(node => string.Equals((string?)node.Attribute("type"), provider, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(node.Value))?.Value.Trim();

    private static string? YoutubeKey(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == "plugin" && uri.Host != "plugin.video.youtube") return null;
        if (uri.Scheme != "plugin" && (uri.Scheme is not ("https" or "http")
            || uri.Host is not ("youtube.com" or "www.youtube.com" or "youtu.be"))) return null;
        var key = uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/') : uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == (uri.Scheme == "plugin" ? "video_id" : "v"))
            .Select(parts => parts[1]).FirstOrDefault();
        return key is { Length: 11 } && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ? key : null;
    }

    private static string? ExistingImage(string? path) =>
        path != null && (File.Exists(path) || Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") ? path : null;

    private static void AddPeople(XElement root, MetadataResult result, string element, CastRole role)
    {
        foreach (var person in root.Elements(element))
        {
            var name = person.Value.Trim();
            if (name.Length > 0) result.Cast.Add(new CastMemberResult
            {
                Name = name, Roles = role,
                TmdbId = PositiveId((string?)person.Attribute("tmdbid")),
                TvdbId = PositiveId((string?)person.Attribute("tvdbid"))
            });
        }
    }

    private static int PositiveId(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
}
