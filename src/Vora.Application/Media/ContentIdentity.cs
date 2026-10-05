using Vora.Domain.Entities.Media;

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

    public static string? Compute(ContentIdentitySource source) =>
        Compute(
            source.Type, source.TmdbId, source.ImdbId, source.TvdbId,
            source.SeasonNumber, source.EpisodeNumber,
            source.SeriesTmdbId, source.SeriesImdbId, source.SeriesTvdbId);

    public static IQueryable<ContentIdentitySource> SelectContentIdentitySource(this IQueryable<MediaItem> items) =>
        items.Select(m => new ContentIdentitySource
        {
            Id = m.Id,
            Type = m is Movie ? "movie"
                : m is TvShow ? "show"
                : m is Season ? "season"
                : m is Episode ? "episode"
                : "other",
            Title = m.Title,
            ReleaseDate = m.ReleaseDate,
            LibraryId = m.LibraryId,
            TmdbId = m.TmdbId,
            ImdbId = m.ImdbId,
            TvdbId = m.TvdbId,
            SeasonNumber = m is Season ? ((Season)m).SeasonNumber
                : m is Episode ? ((Episode)m).Season.SeasonNumber
                : (int?)null,
            EpisodeNumber = m is Episode ? ((Episode)m).EpisodeNumber : (int?)null,
            SeriesTitle = m is Season ? ((Season)m).TvShow.Title
                : m is Episode ? ((Episode)m).Season.TvShow.Title
                : null,
            SeriesReleaseDate = m is Season ? ((Season)m).TvShow.ReleaseDate
                : m is Episode ? ((Episode)m).Season.TvShow.ReleaseDate
                : null,
            SeriesTmdbId = m is Season ? ((Season)m).TvShow.TmdbId
                : m is Episode ? ((Episode)m).Season.TvShow.TmdbId
                : null,
            SeriesImdbId = m is Season ? ((Season)m).TvShow.ImdbId
                : m is Episode ? ((Episode)m).Season.TvShow.ImdbId
                : null,
            SeriesTvdbId = m is Season ? ((Season)m).TvShow.TvdbId
                : m is Episode ? ((Episode)m).Season.TvShow.TvdbId
                : null
        });

    private static string? PickId(string? tmdbId, string? imdbId, string? tvdbId)
    {
        if (!string.IsNullOrWhiteSpace(tmdbId)) return $"tmdb:{tmdbId}";
        if (!string.IsNullOrWhiteSpace(imdbId)) return $"imdb:{imdbId}";
        if (!string.IsNullOrWhiteSpace(tvdbId)) return $"tvdb:{tvdbId}";
        return null;
    }
}

public sealed class ContentIdentitySource
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateOnly? ReleaseDate { get; set; }
    public Guid LibraryId { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? SeriesTitle { get; set; }
    public DateOnly? SeriesReleaseDate { get; set; }
    public string? SeriesTmdbId { get; set; }
    public string? SeriesImdbId { get; set; }
    public string? SeriesTvdbId { get; set; }
}
