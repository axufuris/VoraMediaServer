using System.Linq.Expressions;
using Vora.Application.Actors.ViewModels;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Media;

public class MediaDetailsVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public int? DurationMinutes { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? ContentRating { get; set; }
    public string? Resolution { get; set; }
    public Guid LibraryId { get; set; }
    public string? LibraryArtworkProviderId { get; set; }
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
    public Guid? TvShowId { get; set; }
    public Guid? SeasonId { get; set; }
    public List<EpisodeVM> Episodes { get; set; } = new();
    public List<SeasonVM> Seasons { get; set; } = new();
    public List<CastMemberVM> Cast { get; set; } = new();
    public List<MediaVideoVM> Videos { get; set; } = new();
    public List<MediaExtraVM> Extras { get; set; } = new();
    public List<MediaDetailsPartVM> MediaParts { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<MediaMarkerVM> Markers { get; set; } = new();

    public static Expression<Func<MediaItem, MediaDetailsVM>> Projection =>
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
            PosterUrl = item.PosterUrl,
            BackgroundUrl = item.BackgroundUrl,
            ContentRating = item is Episode ? ((Episode)item).Season.TvShow.ContentRating : item.ContentRating,
            Resolution = item.MediaParts.FirstOrDefault() != null ? item.MediaParts.FirstOrDefault()!.Resolution : null,
            LockedFields = item.LockedFields,
            LibraryId = item.LibraryId,
            LibraryArtworkProviderId = item.Library != null ? item.Library.ArtworkProviderId : null,
            ThirdPartyRating1 = item.ThirdPartyRating1,
            ThirdPartyRating1Name = item.ThirdPartyRating1Name,
            ThirdPartyRating2 = item.ThirdPartyRating2,
            ThirdPartyRating2Name = item.ThirdPartyRating2Name,
            ServerAdminRating = item.ServerAdminRating,
            CollectionIds = item.Collections.Select(c => c.Id).ToList(),
            Genres = item.Genres.Select(g => g.Name).OrderBy(g => g).ToList(),
            Cast = (item is Episode && !item.Cast.Any())
                ? ((Episode)item).Season.TvShow.Cast
                    .OrderBy(c => c.Order)
                    .Select(c => new CastMemberVM
                    {
                        ActorId = c.ActorId,
                        TmdbId = c.Actor != null ? c.Actor.TmdbId : 0,
                        Name = c.Actor != null ? c.Actor.Name : "Unknown Actor",
                        CharacterName = c.CharacterName,
                        Roles = c.Roles,
                        ProfileImageUrl = c.Actor != null ? c.Actor.ProfileImageUrl : null
                    }).ToList()
                : item.Cast
                    .OrderBy(c => c.Order)
                    .Select(c => new CastMemberVM
                    {
                        ActorId = c.ActorId,
                        TmdbId = c.Actor != null ? c.Actor.TmdbId : 0,
                        Name = c.Actor != null ? c.Actor.Name : "Unknown Actor",
                        CharacterName = c.CharacterName,
                        Roles = c.Roles,
                        ProfileImageUrl = c.Actor != null ? c.Actor.ProfileImageUrl : null
                    }).ToList(),
            Videos = item.Videos
                .OrderByDescending(v => v.Type == "Trailer")
                .Select(v => new MediaVideoVM
                {
                    VideoKey = v.VideoKey,
                    Name = v.Name,
                    Site = v.Site,
                    Type = v.Type,
                    IsOfficial = v.IsOfficial
                }).ToList(),
            Extras = item.Extras
                .OrderBy(e => e.ExtraType)
                .ThenBy(e => e.Title)
                .Select(e => new MediaExtraVM
                {
                    Id = e.Id,
                    Title = e.Title,
                    ExtraType = e.ExtraType,
                    Container = e.Parts.Select(p => p.Container).FirstOrDefault()
                }).ToList(),
            NumberOfSeasons = item is TvShow ? ((TvShow)item).Seasons.Count(s => s.MissingSince == null) : (int?)null,
            SeasonNumber = item is Season ? ((Season)item).SeasonNumber :
                           item is Episode ? ((Episode)item).Season.SeasonNumber : null,
            EpisodeNumber = item is Episode ? ((Episode)item).EpisodeNumber : null,
            EndEpisodeNumber = item is Episode ? ((Episode)item).EndEpisodeNumber : null,
            Seasons = item is TvShow ? ((TvShow)item).Seasons.Where(s => s.MissingSince == null).OrderBy(s => s.SeasonNumber).Select(s => new SeasonVM
            {
                Id = s.Id,
                SeasonNumber = s.SeasonNumber,
                Title = s.Title,
                PosterUrl = s.PosterUrl,
                EpisodeCount = s.Episodes.Count(e => e.MissingSince == null),
                ServerAdminRating = s.ServerAdminRating
            }).ToList() : new List<SeasonVM>(),
            TvShowId = item is Season ? ((Season)item).TvShowId :
                       item is Episode ? ((Episode)item).Season.TvShowId : null,
            SeasonId = item is Episode ? ((Episode)item).SeasonId :
                       item is Season ? item.Id : null,
            TvShowTitle = item is Season ? ((Season)item).TvShow.Title :
                          item is Episode ? ((Episode)item).Season.TvShow.Title : null,
            UpcomingEpisodesJson = item is TvShow ? ((TvShow)item).UpcomingEpisodesJson :
                                   item is Season ? ((Season)item).TvShow.UpcomingEpisodesJson :
                                   item is Episode ? ((Episode)item).Season.TvShow.UpcomingEpisodesJson : "[]",
            Episodes = item is Season ? ((Season)item).Episodes.Where(e => e.MissingSince == null).Select(e => new EpisodeVM
            {
                Id = e.Id,
                EpisodeNumber = e.EpisodeNumber,
                EndEpisodeNumber = e.EndEpisodeNumber,
                Title = e.Title,
                Overview = e.Overview,
                PosterUrl = e.PosterUrl,
                ReleaseDate = e.ReleaseDate,
                DurationMinutes = e.Analysis.Duration.HasValue ? (int)e.Analysis.Duration.Value.TotalMinutes : (int?)null,
                ServerAdminRating = e.ServerAdminRating
            }).OrderBy(e => e.EpisodeNumber).ToList() : new List<EpisodeVM>(),
            Markers = item.Markers
                .OrderBy(m => m.Start)
                .Select(m => new MediaMarkerVM
                {
                    Type = m.Type.ToString(),
                    StartSeconds = m.Start.TotalSeconds,
                    EndSeconds = m.End.TotalSeconds,
                    Order = m.Order
                }).ToList(),
            MediaParts = item.MediaParts.Select(p => new MediaDetailsPartVM
            {
                Id = p.Id,
                Resolution = p.Resolution,
                Edition = p.Edition,
                FileSizeBytes = p.FileSizeBytes,
                BitrateKbps = p.OverallBitrate.HasValue ? (int)(p.OverallBitrate.Value / 1000) : (int?)null,
                FilePath = p.FilePath,

                VideoTracks = p.VideoTracks.Select(v => new MediaDetailsPartVideoTrackVM
                {
                    Id = v.Id,
                    Codec = v.Codec,
                    Profile = v.Profile,
                    HdrType = v.HdrType,
                    BitDepth = v.BitDepth,
                    IsDefault = v.IsDefault
                }).ToList(),

                AudioTracks = p.AudioTracks.Select(a => new MediaDetailsPartAudioTrackVM
                {
                    Id = a.Id,
                    Codec = a.Codec,
                    Language = a.Language,
                    Channels = a.Channels,
                    Title = a.Title,
                    IsDefault = a.IsDefault
                }).ToList(),

                SubtitleTracks = p.SubtitleTracks.Select(s => new MediaDetailsPartSubtitleTrackVM
                {
                    Id = s.Id,
                    Codec = s.Codec,
                    Language = s.Language,
                    Title = s.Title,
                    IsForced = s.IsForced,
                    IsDefault = s.IsDefault
                }).ToList()
            }).ToList()
        };
}

public class MediaDetailsPartVideoTrackVM
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? HdrType { get; set; }
    public int? BitDepth { get; set; }
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
}

public class MediaDetailsPartSubtitleTrackVM
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsForced { get; set; }
    public bool IsDefault { get; set; }
}

public class MediaDetailsPartVM
{
    public Guid Id { get; set; }
    public string? Resolution { get; set; }
    public string? Edition { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? BitrateKbps { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public List<MediaDetailsPartVideoTrackVM> VideoTracks { get; set; } = new();
    public List<MediaDetailsPartAudioTrackVM> AudioTracks { get; set; } = new();
    public List<MediaDetailsPartSubtitleTrackVM> SubtitleTracks { get; set; } = new();
}
