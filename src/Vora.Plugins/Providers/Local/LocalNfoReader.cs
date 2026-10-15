using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Local;

public static partial class LocalNfoReader
{
    private const string MovieRoot = "movie";
    private const string ShowRoot = "tvshow";
    private const string SeasonRoot = "season";
    private const string EpisodeRoot = "episodedetails";
    private const string NfoExtension = ".nfo";

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

    public static MetadataResult? ReadMovie(string path, LocalMediaFolders folders) =>
        MovieDocuments(path, folders)
            .Select(candidate => First(folders, candidate, MovieRoot))
            .FirstOrDefault(result => result != null);

    public static MetadataResult? ReadTvShow(string directory, LocalMediaFolders folders)
    {
        var document = LocalMediaAssets.NamedFile(folders.Files(directory), LocalMediaAssets.ShowDocument);
        return document == null ? null : First(folders, document, ShowRoot);
    }

    public static MetadataResult? ReadSeason(string directory, int seasonNumber, LocalMediaFolders folders)
    {
        var document = LocalMediaAssets.NamedFile(folders.Files(directory), LocalMediaAssets.SeasonDocument);
        if (document == null) return null;

        var root = Roots(folders, document, SeasonRoot).FirstOrDefault();
        if (root == null) return null;
        if (Number(root, "seasonnumber") is int declared && declared != seasonNumber) return null;

        return Usable(Parse(root));
    }

    public static MetadataResult? ReadEpisode(string videoPath, int seasonNumber, int episodeNumber, LocalMediaFolders folders)
    {
        var sidecar = Sidecar(videoPath, folders);
        return sidecar == null ? null : Episode(folders, sidecar, seasonNumber, episodeNumber);
    }

    public static EpisodeNumbers? ReadEpisodeNumbers(string videoPath, LocalMediaFolders folders)
    {
        var sidecar = Sidecar(videoPath, folders);
        if (sidecar == null) return null;

        var numbered = Roots(folders, sidecar, EpisodeRoot)
            .Select(root => Number(root, "season") is int season and >= 0 && Number(root, "episode") is int episode and > 0
                ? new EpisodeNumbers(season, episode, null)
                : null)
            .OfType<EpisodeNumbers>()
            .ToList();
        if (numbered.Count == 0) return null;

        var inFirstSeason = numbered.Where(n => n.Season == numbered[0].Season).Select(n => n.Episode).ToList();
        var first = inFirstSeason.Min();
        var last = inFirstSeason.Max();
        return new EpisodeNumbers(numbered[0].Season, first, last > first ? last : null);
    }

    public static IEnumerable<string> MovieDocuments(string videoPath, LocalMediaFolders folders)
    {
        var files = folders.Files(Path.GetDirectoryName(videoPath));

        var sidecar = Sidecar(videoPath, folders);
        if (sidecar != null) yield return sidecar;

        var shared = LocalMediaAssets.NamedFile(files, LocalMediaAssets.MovieDocument);
        if (shared != null && LocalMediaAssets.HoldsOneFilm(files, videoPath)) yield return shared;
    }

    public static string? Sidecar(string videoPath, LocalMediaFolders folders) =>
        LocalMediaAssets.NamedFile(folders.Files(Path.GetDirectoryName(videoPath)), Path.GetFileNameWithoutExtension(videoPath) + NfoExtension);

    private static IEnumerable<XElement> Roots(LocalMediaFolders folders, string path, string rootName) =>
        folders.Document(path).Where(root => root.Name == rootName);

    private static MetadataResult? First(LocalMediaFolders folders, string path, string rootName)
    {
        var root = Roots(folders, path, rootName).FirstOrDefault();
        return root == null ? null : Usable(Parse(root));
    }

    private static MetadataResult? Episode(LocalMediaFolders folders, string path, int seasonNumber, int episodeNumber)
    {
        var roots = Roots(folders, path, EpisodeRoot).ToList();
        var root = roots.FirstOrDefault(r => Number(r, "episode") == episodeNumber && (Number(r, "season") ?? seasonNumber) == seasonNumber)
            ?? (roots.Count == 1 ? roots[0] : null);
        return root == null ? null : Usable(Parse(root));
    }

    private static MetadataResult? Usable(MetadataResult result) =>
        result.Title != null || result.Overview != null || result.TmdbId != null || result.ImdbId != null
            || result.TvdbId != null || result.ReleaseDate != null || result.Cast.Count > 0
            ? result
            : null;

    private static MetadataResult Parse(XElement root)
    {
        var result = new MetadataResult
        {
            Title = Text(root, "title"), OriginalTitle = Text(root, "originaltitle"),
            SortTitle = Text(root, "sorttitle"), Overview = Text(root, "plot"),
            Tagline = Text(root, "tagline"), ContentRating = Certification(Text(root, "mpaa")), Status = Text(root, "status"),
            TmdbId = ProviderId(root, "tmdb"), ImdbId = ProviderId(root, "imdb") ?? LegacyImdbId(root), TvdbId = ProviderId(root, "tvdb")
        };
        foreach (var name in new[] { "premiered", "releasedate", "aired" })
        {
            if (DateOnly.TryParseExact(Text(root, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                result.ReleaseDate = date;
                break;
            }
        }
        if (result.ReleaseDate == null && Number(root, "year") is int year and >= 1 and <= 9999)
            result.ReleaseDate = new DateOnly(year, 1, 1);
        if (Number(root, "runtime") is int runtime and > 0)
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
                ProfileImageUrl = RemoteImage(Text(actor, "thumb")), Roles = CastRole.Actor
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
        if (collectionName != null && Number(root, "collectionnumber") is int collectionId and > 0)
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

    private static int? Number(XElement root, string name) =>
        int.TryParse(Text(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? ProviderId(XElement root, string provider) =>
        Text(root, provider + "id")
        ?? Text(root, provider + "_id")
        ?? root.Elements("uniqueid").FirstOrDefault(node => string.Equals((string?)node.Attribute("type"), provider, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(node.Value))?.Value.Trim();

    private static string? Certification(string? value)
    {
        if (value == null) return null;
        var match = CertificationText().Match(value);
        return match.Success ? match.Groups["Rating"].Value : null;
    }

    [GeneratedRegex(@"^(?:rated\s+|[a-z]{2}[:/]\s*)?(?<Rating>[a-z0-9+\-]{1,10})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CertificationText();

    private static string? LegacyImdbId(XElement root)
    {
        var id = Text(root, "id");
        return id != null && id.StartsWith("tt", StringComparison.OrdinalIgnoreCase) && id.Skip(2).All(char.IsAsciiDigit) && id.Length > 2 ? id : null;
    }

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

    private static string? RemoteImage(string? address) =>
        address != null && Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? address : null;

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
