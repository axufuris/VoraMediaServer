using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Vora.Application.Podcasts;

public class PodcastFeedResult
{
    public required string Title { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? HomepageUrl { get; init; }
    public string? Language { get; init; }
    public List<PodcastFeedEpisode> Episodes { get; init; } = new();
}

public class PodcastFeedEpisode
{
    public required string Guid { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string AudioUrl { get; init; }
    public string? ArtworkUrl { get; init; }
    public int? DurationSeconds { get; init; }
    public DateTime? PublishedAt { get; init; }
    public int? EpisodeNumber { get; init; }
    public int? SeasonNumber { get; init; }
}

public static class PodcastFeedParser
{
    private static readonly XNamespace ItunesNs = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";
    private static readonly Regex DurationRegex = new(@"^(?:(\d+):)?(\d+):(\d+)$", RegexOptions.Compiled);

    public static PodcastFeedResult Parse(string rssXml)
    {
        var doc = XDocument.Parse(rssXml);
        var channel = doc.Root?.Element("channel")
            ?? throw new InvalidOperationException("RSS feed missing <channel> element.");

        var title = (string?)channel.Element("title") ?? "Untitled Podcast";
        var description = (string?)channel.Element("description")
            ?? (string?)channel.Element(ItunesNs + "summary");
        var homepage = (string?)channel.Element("link");
        var language = (string?)channel.Element("language");
        var author = (string?)channel.Element(ItunesNs + "author")
            ?? (string?)channel.Element("managingEditor");
        var artwork = ExtractArtwork(channel);

        var episodes = new List<PodcastFeedEpisode>();
        foreach (var item in channel.Elements("item"))
        {
            var ep = ParseEpisode(item);
            if (ep != null) episodes.Add(ep);
        }

        return new PodcastFeedResult
        {
            Title = title,
            Author = author,
            Description = description,
            ArtworkUrl = artwork,
            HomepageUrl = homepage,
            Language = language,
            Episodes = episodes
        };
    }

    private static PodcastFeedEpisode? ParseEpisode(XElement item)
    {
        var audioUrl = (string?)item.Element("enclosure")?.Attribute("url");
        if (string.IsNullOrWhiteSpace(audioUrl)) return null;

        var title = (string?)item.Element("title") ?? "Untitled Episode";
        var guidElement = item.Element("guid");
        var guid = (string?)guidElement ?? audioUrl;
        if (string.IsNullOrWhiteSpace(guid)) guid = audioUrl;

        var description = (string?)item.Element(ContentNs + "encoded")
            ?? (string?)item.Element("description")
            ?? (string?)item.Element(ItunesNs + "summary");

        var pubDateRaw = (string?)item.Element("pubDate");
        DateTime? publishedAt = null;
        if (!string.IsNullOrWhiteSpace(pubDateRaw)
            && DateTime.TryParse(pubDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            publishedAt = parsedDate;
        }

        var durationRaw = (string?)item.Element(ItunesNs + "duration");
        var duration = ParseDuration(durationRaw);

        var artwork = (string?)item.Element(ItunesNs + "image")?.Attribute("href");

        var episodeNumberRaw = (string?)item.Element(ItunesNs + "episode");
        int? episodeNumber = int.TryParse(episodeNumberRaw, out var en) ? en : null;
        var seasonNumberRaw = (string?)item.Element(ItunesNs + "season");
        int? seasonNumber = int.TryParse(seasonNumberRaw, out var sn) ? sn : null;

        return new PodcastFeedEpisode
        {
            Guid = guid,
            Title = title,
            Description = description,
            AudioUrl = audioUrl,
            ArtworkUrl = artwork,
            DurationSeconds = duration,
            PublishedAt = publishedAt,
            EpisodeNumber = episodeNumber,
            SeasonNumber = seasonNumber
        };
    }

    private static int? ParseDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (int.TryParse(raw, out var seconds)) return seconds;

        var match = DurationRegex.Match(raw.Trim());
        if (!match.Success) return null;

        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        var minutes = int.Parse(match.Groups[2].Value);
        var secs = int.Parse(match.Groups[3].Value);

        return hours * 3600 + minutes * 60 + secs;
    }

    private static string? ExtractArtwork(XElement channel)
    {
        var itunesImage = (string?)channel.Element(ItunesNs + "image")?.Attribute("href");
        if (!string.IsNullOrWhiteSpace(itunesImage)) return itunesImage;

        var standardImage = (string?)channel.Element("image")?.Element("url");
        return string.IsNullOrWhiteSpace(standardImage) ? null : standardImage;
    }
}
