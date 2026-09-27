using System.Text.RegularExpressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Metadata;

public sealed record MediaMatchIdQuery(string Source, string ExternalId, bool? IsTvShow);

public static partial class MediaMatchIds
{
    public const string Tmdb = "tmdb";
    public const string Tvdb = "tvdb";
    public const string Imdb = "imdb";

    [GeneratedRegex(@"\b(tt\d{5,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbIdPattern();

    [GeneratedRegex(@"themoviedb\.org/(movie|tv)/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TmdbUrlPattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"\s*[\[{](?:imdb|tmdb|tvdb)(?:id)?-[^\]}]*[\]}]", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalIdTagPattern();

    [GeneratedRegex(@"\s*\((?:19|20)\d{2}\)\s*$")]
    private static partial Regex TrailingYearPattern();

    public static string? NormalizeSource(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        Tmdb => Tmdb,
        Tvdb => Tvdb,
        Imdb => Imdb,
        _ => null
    };

    public static string? NormalizeId(string source, string? externalId)
    {
        var value = externalId?.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        switch (source)
        {
            case Imdb:
                var imdb = ImdbIdPattern().Match(value);
                return imdb.Success ? imdb.Groups[1].Value.ToLowerInvariant() : null;
            case Tmdb:
                var tmdbUrl = TmdbUrlPattern().Match(value);
                if (tmdbUrl.Success) return tmdbUrl.Groups[2].Value;
                return DigitsPattern().IsMatch(value) ? value : null;
            case Tvdb:
                return DigitsPattern().IsMatch(value) ? value : null;
            default:
                return null;
        }
    }

    public static MediaMatchIdQuery? FromQuery(string? query)
    {
        var value = query?.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        var imdb = ImdbIdPattern().Match(value);
        if (imdb.Success && (imdb.Value.Length == value.Length || value.Contains("imdb.com", StringComparison.OrdinalIgnoreCase)))
        {
            return new MediaMatchIdQuery(Imdb, imdb.Groups[1].Value.ToLowerInvariant(), null);
        }

        var tmdb = TmdbUrlPattern().Match(value);
        if (tmdb.Success)
        {
            var isTvShow = string.Equals(tmdb.Groups[1].Value, "tv", StringComparison.OrdinalIgnoreCase);
            return new MediaMatchIdQuery(Tmdb, tmdb.Groups[2].Value, isTvShow);
        }

        return null;
    }

    public static string CleanSearchTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var cleaned = ExternalIdTagPattern().Replace(title, string.Empty);
        cleaned = TrailingYearPattern().Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }

    public static string Label(string source) => source switch
    {
        Tmdb => "TMDB",
        Tvdb => "TVDB",
        Imdb => "IMDb",
        _ => source
    };

    public static void Assign(MediaItem item, string source, string externalId)
    {
        switch (source)
        {
            case Tmdb:
                item.TmdbId = externalId;
                break;
            case Tvdb:
                item.TvdbId = externalId;
                break;
            case Imdb:
                item.ImdbId = externalId;
                break;
            default:
                throw new ArgumentException($"Unknown match source '{source}'.", nameof(source));
        }
    }
}
