namespace Vora.Application.Media;

public static class ContentIdentity
{
    public static string? Compute(
        string type,
        string? tmdbId, string? imdbId, string? tvdbId,
        int? seasonNumber, int? episodeNumber,
        string? seriesTmdbId, string? seriesImdbId, string? seriesTvdbId)
    {
        switch (type)
        {
            case "movie":
            case "show":
            {
                var key = PickId(tmdbId, imdbId, tvdbId);
                return key == null ? null : $"{type}:{key}";
            }
            case "season":
            {
                var series = PickId(seriesTmdbId, seriesImdbId, seriesTvdbId);
                return series == null || seasonNumber == null ? null : $"season:{series}:{seasonNumber}";
            }
            case "episode":
            {
                var series = PickId(seriesTmdbId, seriesImdbId, seriesTvdbId);
                return series == null || seasonNumber == null || episodeNumber == null
                    ? null
                    : $"episode:{series}:{seasonNumber}:{episodeNumber}";
            }
            default:
                return null;
        }
    }

    private static string? PickId(string? tmdbId, string? imdbId, string? tvdbId)
    {
        if (!string.IsNullOrWhiteSpace(tmdbId)) return $"tmdb:{tmdbId}";
        if (!string.IsNullOrWhiteSpace(imdbId)) return $"imdb:{imdbId}";
        if (!string.IsNullOrWhiteSpace(tvdbId)) return $"tvdb:{tvdbId}";
        return null;
    }
}
