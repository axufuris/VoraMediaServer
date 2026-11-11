using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Actors.ViewModels;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Postgres;

// The detail page as it loaded before MediaDetailsQuery: one projection holding
// every list, which EF ran as 26 split queries, then the profile's watch state
// attached with a few more. Kept verbatim so PostgresMediaDetailsTests can check
// the one statement shows the same page.
internal static class MediaDetailsOracle
{
    public static async Task<MediaDetailsVM?> LoadAsync(VoraDbContext db, Guid id, Guid? profileId)
    {
        var vm = await db.MediaItems.AsNoTracking().AsSplitQuery()
            .Where(m => m.Id == id && m.MissingSince == null)
            .Select(Projection)
            .FirstOrDefaultAsync();
        if (vm != null && profileId.HasValue) await AttachAsync(db, vm, profileId.Value);
        return vm;
    }

    private static Expression<Func<MediaItem, MediaDetailsVM>> Projection =>
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
            CollectionIds = item.Collections.Select(c => c.Id).ToList(),
            // An episode carries almost none of this itself and a season carries
            // none at all, so both inherit from the show rather than rendering a
            // blank credits block. Own value first, then the season, then the show.
            Genres = item.Genres.Any()
                ? item.Genres.Select(g => g.Name).OrderBy(g => g).ToList()
                : item is Episode
                    ? (((Episode)item).Season.Genres.Any()
                        ? ((Episode)item).Season.Genres.Select(g => g.Name).OrderBy(g => g).ToList()
                        : ((Episode)item).Season.TvShow.Genres.Select(g => g.Name).OrderBy(g => g).ToList())
                    : item is Season
                        ? ((Season)item).TvShow.Genres.Select(g => g.Name).OrderBy(g => g).ToList()
                        : new List<string>(),

            Studios = item.ProductionCompanies.Any()
                ? item.ProductionCompanies.Select(c => c.Name).OrderBy(c => c).ToList()
                : item is Episode
                    ? (((Episode)item).Season.ProductionCompanies.Any()
                        ? ((Episode)item).Season.ProductionCompanies.Select(c => c.Name).OrderBy(c => c).ToList()
                        : ((Episode)item).Season.TvShow.ProductionCompanies.Select(c => c.Name).OrderBy(c => c).ToList())
                    : item is Season
                        ? ((Season)item).TvShow.ProductionCompanies.Select(c => c.Name).OrderBy(c => c).ToList()
                        : new List<string>(),

            Directors = item.Cast.Any(c => (c.Roles & MediaCastRole.Director) != 0)
                ? item.Cast.Where(c => (c.Roles & MediaCastRole.Director) != 0).OrderBy(c => c.Order)
                    .Select(c => c.Actor != null ? c.Actor.Name : "Unknown").ToList()
                : item is Episode
                    ? (((Episode)item).Season.Cast.Any(c => (c.Roles & MediaCastRole.Director) != 0)
                        ? ((Episode)item).Season.Cast.Where(c => (c.Roles & MediaCastRole.Director) != 0).OrderBy(c => c.Order)
                            .Select(c => c.Actor != null ? c.Actor.Name : "Unknown").ToList()
                        : ((Episode)item).Season.TvShow.Cast.Where(c => (c.Roles & MediaCastRole.Director) != 0).OrderBy(c => c.Order)
                            .Select(c => c.Actor != null ? c.Actor.Name : "Unknown").ToList())
                    : item is Season
                        ? ((Season)item).TvShow.Cast.Where(c => (c.Roles & MediaCastRole.Director) != 0).OrderBy(c => c.Order)
                            .Select(c => c.Actor != null ? c.Actor.Name : "Unknown").ToList()
                        : new List<string>(),
            // A season never has a cast of its own, and an episode only has one
            // when the provider returned guest credits, so both fall back to the
            // show rather than rendering an empty Cast & Crew row.
            Cast = item is Season
                ? ((Season)item).TvShow.Cast
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
                : (item is Episode && !item.Cast.Any())
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
                EpisodeCount = s.Episodes.Where(e => e.MissingSince == null).Sum(e => (e.EndEpisodeNumber ?? e.EpisodeNumber) - e.EpisodeNumber + 1),
                ServerAdminRating = s.ServerAdminRating
            }).ToList() : new List<SeasonVM>(),
            TvShowId = item is Season ? ((Season)item).TvShowId :
                       item is Episode ? ((Episode)item).Season.TvShowId : null,
            SeasonId = item is Episode ? ((Episode)item).SeasonId :
                       item is Season ? item.Id : null,
            TvShowTitle = item is Season ? ((Season)item).TvShow.Title :
                          item is Episode ? ((Episode)item).Season.TvShow.Title : null,
            TvShowOverview = item is Season ? ((Season)item).TvShow.Overview : null,
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
                VersionName = p.VersionName,
                Container = p.Container,
                DurationSeconds = p.Duration.HasValue ? p.Duration.Value.TotalSeconds : (double?)null,
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
                    BitrateKbps = v.Bitrate.HasValue ? (int)(v.Bitrate.Value / 1000) : (int?)null,
                    Width = v.Width,
                    Height = v.Height,
                    IsDefault = v.IsDefault
                }).ToList(),

                AudioTracks = p.AudioTracks.Select(a => new MediaDetailsPartAudioTrackVM
                {
                    Id = a.Id,
                    Codec = a.Codec,
                    Language = a.Language,
                    Channels = a.Channels,
                    Title = a.Title,
                    IsDefault = a.IsDefault,
                    EmbeddedPosition = p.AudioTracks.Count(other => other.StreamIndex < a.StreamIndex)
                }).ToList(),

                SubtitleTracks = p.SubtitleTracks.Select(s => new MediaDetailsPartSubtitleTrackVM
                {
                    Id = s.Id,
                    Codec = s.Codec,
                    Language = s.Language,
                    Title = s.Title,
                    IsForced = s.IsForced,
                    IsHearingImpaired = s.IsHearingImpaired,
                    IsDefault = s.IsDefault,
                    IsExternal = s.ExternalFilePath != null,
                    IsDownloaded = s.IsDownloaded,
                    EmbeddedPosition = s.ExternalFilePath == null
                        ? (int?)p.SubtitleTracks.Count(other => other.ExternalFilePath == null && other.StreamIndex < s.StreamIndex)
                        : null
                }).ToList()
            }).ToList()
        };

    private static async Task AttachAsync(VoraDbContext _context, MediaDetailsVM vm, Guid profileId)
    {
        var rating = await _context.Set<UserMediaRating>()
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.MediaItemId == vm.Id)
            .Select(r => (decimal?)r.Rating)
            .FirstOrDefaultAsync();
        vm.MyRating = rating;

        if (vm.Type == "Movie" || vm.Type == "Episode")
        {
            var state = await _context.Set<UserMediaState>()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.MediaItemId == vm.Id);

            if (state != null)
            {
                vm.IsPlayed = state.IsPlayed;
                vm.ResumePositionSeconds = state.ResumePositionSeconds;
            }
        }
        else if (vm.Type == "TvShow")
        {
            var episodeMappings = await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => e.Season.TvShowId == vm.Id && e.MissingSince == null)
                .Select(e => new { e.Id, e.SeasonId })
                .ToListAsync();

            if (episodeMappings.Count > 0)
            {
                var allEpisodeIds = episodeMappings.Select(e => e.Id).ToList();

                var playedEpisodeIds = await _context.Set<UserMediaState>()
                    .AsNoTracking()
                    .Where(s => s.ProfileId == profileId && s.IsPlayed && allEpisodeIds.Contains(s.MediaItemId))
                    .Select(s => s.MediaItemId)
                    .ToListAsync();

                var playedSet = playedEpisodeIds.ToHashSet();

                var statsBySeason = episodeMappings
                    .GroupBy(x => x.SeasonId)
                    .ToDictionary(g => g.Key, g => new
                    {
                        TotalEpisodes = g.Count(),
                        PlayedEpisodes = g.Count(x => playedSet.Contains(x.Id))
                    });

                foreach (var season in vm.Seasons)
                {
                    if (statsBySeason.TryGetValue(season.Id, out var stats))
                    {
                        season.UnplayedItemCount = stats.TotalEpisodes - stats.PlayedEpisodes;
                        season.IsPlayed = stats.TotalEpisodes > 0 && stats.TotalEpisodes == stats.PlayedEpisodes;
                    }
                }

                int totalUnplayed = statsBySeason.Values.Sum(x => x.TotalEpisodes - x.PlayedEpisodes);
                int totalEpisodes = statsBySeason.Values.Sum(x => x.TotalEpisodes);

                vm.UnplayedItemCount = totalUnplayed;
                vm.IsPlayed = totalEpisodes > 0 && totalUnplayed == 0;
            }

            var seasonIds = vm.Seasons.Select(s => s.Id).ToList();
            if (seasonIds.Count > 0)
            {
                var seasonRatings = await _context.Set<UserMediaRating>()
                    .AsNoTracking()
                    .Where(r => r.ProfileId == profileId && seasonIds.Contains(r.MediaItemId))
                    .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);
                foreach (var s in vm.Seasons)
                {
                    if (seasonRatings.TryGetValue(s.Id, out var seasonRating)) s.MyRating = seasonRating;
                }
            }
        }
        else if (vm.Type == "Season")
        {
            var episodeIds = vm.Episodes.Select(e => e.Id).ToList();

            var states = await _context.Set<UserMediaState>()
                .AsNoTracking()
                .Where(s => s.ProfileId == profileId && episodeIds.Contains(s.MediaItemId))
                .ToDictionaryAsync(s => s.MediaItemId);

            var episodeRatings = await _context.Set<UserMediaRating>()
                .AsNoTracking()
                .Where(r => r.ProfileId == profileId && episodeIds.Contains(r.MediaItemId))
                .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);

            int unplayedTotal = 0;

            foreach (var ep in vm.Episodes)
            {
                if (states.TryGetValue(ep.Id, out var state))
                {
                    ep.IsPlayed = state.IsPlayed;
                    ep.ResumePositionSeconds = state.ResumePositionSeconds;
                }
                if (episodeRatings.TryGetValue(ep.Id, out var episodeRating))
                {
                    ep.MyRating = episodeRating;
                }
                if (!ep.IsPlayed) unplayedTotal++;
            }

            vm.UnplayedItemCount = unplayedTotal;
            vm.IsPlayed = unplayedTotal == 0 && vm.Episodes.Count > 0;
        }
    }
}
