using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Metadata;

public static class MetadataReset
{
    public static void ClearTitle(MediaItem title, string? fileTitle, int? fileYear, IReadOnlySet<string> uploadedArtwork)
    {
        ClearLookedUp(title, uploadedArtwork);

        if (!string.IsNullOrWhiteSpace(fileTitle)) Unless(title, nameof(title.Title), () => title.Title = fileTitle.Trim());
        Unless(title, nameof(title.SortTitle), () => title.SortTitle = null);
        Unless(title, nameof(title.OriginalTitle), () => title.OriginalTitle = null);
        Unless(title, nameof(title.ReleaseDate), () => title.ReleaseDate = fileYear is int year ? new DateOnly(year, 1, 1) : null);

        switch (title)
        {
            case Movie movie:
                Unless(movie, nameof(movie.Budget), () => movie.Budget = null);
                Unless(movie, nameof(movie.Revenue), () => movie.Revenue = null);
                Unless(movie, nameof(movie.TheatricalReleaseDate), () => movie.TheatricalReleaseDate = null);
                Unless(movie, nameof(movie.DigitalReleaseDate), () => movie.DigitalReleaseDate = null);
                break;
            case TvShow show:
                Unless(show, nameof(show.TvType), () => show.TvType = null);
                Unless(show, nameof(show.InProduction), () => show.InProduction = null);
                Unless(show, nameof(show.NumberOfSeasons), () => show.NumberOfSeasons = null);
                Unless(show, nameof(show.NumberOfEpisodes), () => show.NumberOfEpisodes = null);
                Unless(show, nameof(show.LastAirDate), () => show.LastAirDate = null);
                Unless(show, nameof(show.NextAirDate), () => show.NextAirDate = null);
                Unless(show, nameof(show.LastEpisodeToAirName), () => show.LastEpisodeToAirName = null);
                Unless(show, nameof(show.NextEpisodeToAirName), () => show.NextEpisodeToAirName = null);
                show.UpcomingEpisodesJson = "[]";
                show.Networks.Clear();
                break;
        }
    }

    public static void ClearSeason(Season season, IReadOnlySet<string> uploadedArtwork)
    {
        ClearLookedUp(season, uploadedArtwork);

        var name = SeasonNames.Default(season.SeasonNumber);
        Unless(season, nameof(season.Title), () => season.Title = name);
        Unless(season, nameof(season.SortTitle), () => season.SortTitle = name);
        Unless(season, nameof(season.OriginalTitle), () => season.OriginalTitle = name);
        Unless(season, nameof(season.ReleaseDate), () => season.ReleaseDate = null);
        Unless(season, nameof(season.VoteAverage), () => season.VoteAverage = null);
    }

    public static void ClearEpisode(Episode episode, string fileTitle, DateOnly? fileAirDate, IReadOnlySet<string> uploadedArtwork)
    {
        ClearLookedUp(episode, uploadedArtwork);

        Unless(episode, nameof(episode.Title), () => episode.Title = fileTitle);
        Unless(episode, nameof(episode.SortTitle), () => episode.SortTitle = fileTitle);
        Unless(episode, nameof(episode.OriginalTitle), () => episode.OriginalTitle = fileTitle);
        Unless(episode, nameof(episode.ReleaseDate), () => episode.ReleaseDate = fileAirDate);
    }

    public static string EpisodeTitleFallback(string showTitle, int seasonNumber, int episodeNumber) =>
        $"{showTitle} - S{seasonNumber:D2}E{episodeNumber:D2}";

    private static void ClearLookedUp(MediaItem item, IReadOnlySet<string> uploadedArtwork)
    {
        Unless(item, nameof(item.Overview), () => item.Overview = null);
        Unless(item, nameof(item.Tagline), () => item.Tagline = null);
        Unless(item, nameof(item.OriginalLanguage), () => item.OriginalLanguage = null);
        Unless(item, nameof(item.Status), () => item.Status = null);
        Unless(item, nameof(item.HomePage), () => item.HomePage = null);
        Unless(item, nameof(item.ContentRating), () => item.ContentRating = null);
        Unless(item, nameof(item.IsAdult), () => item.IsAdult = false);
        Unless(item, nameof(item.TmdbId), () => item.TmdbId = null);
        Unless(item, nameof(item.ImdbId), () => item.ImdbId = null);
        Unless(item, nameof(item.TvdbId), () => item.TvdbId = null);
        Unless(item, nameof(item.ThirdPartyRating1), () =>
        {
            item.ThirdPartyRating1 = null;
            item.ThirdPartyRating1Name = null;
        });
        Unless(item, nameof(item.ThirdPartyRating2), () =>
        {
            item.ThirdPartyRating2 = null;
            item.ThirdPartyRating2Name = null;
        });
        item.RatingsCheckedAt = null;

        if (!IsUploaded(item.OriginalPosterUrl ?? item.PosterUrl, uploadedArtwork))
        {
            Unless(item, nameof(item.PosterUrl), () =>
            {
                item.PosterUrl = null;
                item.OriginalPosterUrl = null;
            });
        }
        if (!IsUploaded(item.BackgroundUrl, uploadedArtwork)) Unless(item, nameof(item.BackgroundUrl), () => item.BackgroundUrl = null);
        if (!IsUploaded(item.LogoUrl, uploadedArtwork)) Unless(item, nameof(item.LogoUrl), () => item.LogoUrl = null);

        Unless(item, nameof(item.Genres), () => item.Genres.Clear());
        item.ProductionCompanies.Clear();
        item.OriginCountries.Clear();

        item.LastMetadataRefresh = null;
        item.FullyRefreshedAt = null;
        item.LastOverlayGeneratedAt = null;
    }

    private static bool IsUploaded(string? url, IReadOnlySet<string> uploadedArtwork) =>
        url != null && uploadedArtwork.Contains(url);

    private static void Unless(MediaItem item, string field, Action clear)
    {
        if (!item.IsLocked(field)) clear();
    }
}
