using System.Linq.Expressions;
using Vora.Application.Actors.ViewModels;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

// What IMediaRepository.GetMediaDetailsAsync loads: the page, and the episodes
// of its show on disk, which MediaManager takes off the show's Upcoming list.
public sealed record MediaDetailsLoad(MediaDetailsVM Details, IReadOnlyCollection<HeldEpisode> HeldEpisodes);

public class MediaDetailsVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public int? DurationMinutes { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? ContentRating { get; set; }
    public string? Resolution { get; set; }
    public Guid LibraryId { get; set; }
    public string? LibraryArtworkProviderId { get; set; }
    public bool EnablePreviewDetection { get; set; }
    public List<Guid> CollectionIds { get; set; } = new();
    public List<string> LockedFields { get; set; } = new();
    public decimal? ThirdPartyRating1 { get; set; }
    public string? ThirdPartyRating1Name { get; set; }
    public decimal? ThirdPartyRating2 { get; set; }
    public string? ThirdPartyRating2Name { get; set; }
    public decimal? ServerAdminRating { get; set; }
    public decimal? MyRating { get; set; }

    public bool IsPlayed { get; set; }
    public double? ResumePositionSeconds { get; set; }
    public int? UnplayedItemCount { get; set; }
    public string? UpcomingEpisodesJson { get; set; }
    public int? NumberOfSeasons { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? EndEpisodeNumber { get; set; }
    public string? TvShowTitle { get; set; }
    public string? TvShowOverview { get; set; }
    public Guid? TvShowId { get; set; }
    public Guid? SeasonId { get; set; }
    public List<EpisodeVM> Episodes { get; set; } = new();
    public List<SeasonVM> Seasons { get; set; } = new();
    public List<CastMemberVM> Cast { get; set; } = new();
    public List<MediaVideoVM> Videos { get; set; } = new();
    public List<MediaExtraVM> Extras { get; set; } = new();
    public List<MediaDetailsPartVM> MediaParts { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> Studios { get; set; } = new();

    // Resolved separately from Cast so an episode that has its own guest cast
    // but no directing credit still shows a director, without the show's crew
    // being mixed into the episode's cast row.
    public List<string> Directors { get; set; } = new();
    public List<MediaMarkerVM> Markers { get; set; } = new();

    // The item's own fields, and what it shows of its season's and show's, in
    // one query through the access filter. Its lists (credits, seasons,
    // episodes, files) and the viewer's watch state come in one more
    // (IMediaRepository.GetMediaDetailsAsync): projected here as well, they
    // cost a query each, one per fallback, 26 for any item.
    public static Expression<Func<MediaItem, MediaDetailsVM>> Fields =>
        item => new MediaDetailsVM
        {
            Id = item.Id,
            Title = item.Title,
            SortTitle = item.SortTitle,
            Overview = item.Overview,
            DurationMinutes = item.Analysis.Duration.HasValue ? (int)item.Analysis.Duration.Value.TotalMinutes : (int?)null,
            ReleaseDate = item.ReleaseDate,
            Type = item is Movie ? "Movie" :
                   item is TvShow ? "TvShow" :
                   item is Season ? "Season" :
                   item is Episode ? "Episode" : "Unknown",
            PosterUrl = item.PosterUrl ?? (item is Season ? ((Season)item).TvShow.PosterUrl : null),
            BackgroundUrl = item.BackgroundUrl
                ?? (item is Episode ? ((Episode)item).Season.TvShow.BackgroundUrl
                    : item is Season ? ((Season)item).TvShow.BackgroundUrl
                    : null),
            LogoUrl = item is Episode ? ((Episode)item).Season.TvShow.LogoUrl
                : item is Season ? ((Season)item).TvShow.LogoUrl
                : item.LogoUrl,
            ContentRating = item is Episode ? ((Episode)item).Season.TvShow.ContentRating : item.ContentRating,
            Resolution = item.MediaParts.FirstOrDefault() != null ? item.MediaParts.FirstOrDefault()!.Resolution : null,
            LockedFields = item.LockedFields,
            LibraryId = item.LibraryId,
            LibraryArtworkProviderId = item.Library != null ? item.Library.ArtworkProviderId : null,
            EnablePreviewDetection = item.Library != null && item.Library.EnablePreviewDetection,
            ThirdPartyRating1 = item.ThirdPartyRating1,
            ThirdPartyRating1Name = item.ThirdPartyRating1Name,
            ThirdPartyRating2 = item.ThirdPartyRating2,
            ThirdPartyRating2Name = item.ThirdPartyRating2Name,
            ServerAdminRating = item.ServerAdminRating,
            NumberOfSeasons = item is TvShow ? ((TvShow)item).Seasons.Count(s => s.MissingSince == null) : (int?)null,
            SeasonNumber = item is Season ? ((Season)item).SeasonNumber :
                           item is Episode ? ((Episode)item).Season.SeasonNumber : null,
            EpisodeNumber = item is Episode ? ((Episode)item).EpisodeNumber : null,
            EndEpisodeNumber = item is Episode ? ((Episode)item).EndEpisodeNumber : null,
            TvShowId = item is Season ? ((Season)item).TvShowId :
                       item is Episode ? ((Episode)item).Season.TvShowId : null,
            SeasonId = item is Episode ? ((Episode)item).SeasonId :
                       item is Season ? item.Id : null,
            TvShowTitle = item is Season ? ((Season)item).TvShow.Title :
                          item is Episode ? ((Episode)item).Season.TvShow.Title : null,
            TvShowOverview = item is Season ? ((Season)item).TvShow.Overview : null,
            UpcomingEpisodesJson = item is TvShow ? ((TvShow)item).UpcomingEpisodesJson :
                                   item is Season ? ((Season)item).TvShow.UpcomingEpisodesJson :
                                   item is Episode ? ((Episode)item).Season.TvShow.UpcomingEpisodesJson : "[]"
        };
}

public class MediaDetailsPartVideoTrackVM
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? HdrType { get; set; }
    public int? BitDepth { get; set; }
    public int? BitrateKbps { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool IsDefault { get; set; }
}

public class MediaDetailsPartAudioTrackVM
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public int? Channels { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public int EmbeddedPosition { get; set; }
}

public class MediaDetailsPartSubtitleTrackVM
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsDefault { get; set; }
    public bool IsExternal { get; set; }
    public bool IsDownloaded { get; set; }
    public int? EmbeddedPosition { get; set; }
}

public class MediaDetailsPartVM
{
    public Guid Id { get; set; }
    public string? Resolution { get; set; }
    public string? Edition { get; set; }
    public string? VersionName { get; set; }
    public string? Container { get; set; }
    public double? DurationSeconds { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? BitrateKbps { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public List<MediaDetailsPartVideoTrackVM> VideoTracks { get; set; } = new();
    public List<MediaDetailsPartAudioTrackVM> AudioTracks { get; set; } = new();
    public List<MediaDetailsPartSubtitleTrackVM> SubtitleTracks { get; set; } = new();
}
