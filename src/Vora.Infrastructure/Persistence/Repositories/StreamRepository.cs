using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class StreamRepository(VoraDbContext context) : IStreamRepository
{
    public async Task<Guid> ResolvePlayableMediaIdAsync(Guid mediaId, Guid? profileId)
    {
        var episodes = context.Set<Episode>().AsNoTracking().Where(e => e.MissingSince == null);
        if (await context.MediaItems.AsNoTracking().AnyAsync(m => m.Id == mediaId && m is TvShow))
        {
            episodes = episodes.Where(e => e.Season.TvShowId == mediaId);
        }
        else if (await context.MediaItems.AsNoTracking().AnyAsync(m => m.Id == mediaId && m is Season))
        {
            episodes = episodes.Where(e => e.SeasonId == mediaId);
        }
        else
        {
            return mediaId;
        }

        Guid? episodeId = null;
        if (profileId.HasValue)
        {
            episodeId = await episodes
                .Where(e => !context.UserMediaStates.Any(s => s.ProfileId == profileId.Value && s.MediaItemId == e.Id && s.IsPlayed))
                .OrderBy(e => e.Season.SeasonNumber).ThenBy(e => e.EpisodeNumber)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync();
        }

        episodeId ??= await episodes
            .OrderBy(e => e.Season.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync();

        return episodeId ?? mediaId;
    }

    public async Task<Guid> ResolveTitleIdAsync(Guid mediaId)
    {
        var episodeShow = await context.Set<Episode>().AsNoTracking()
            .Where(e => e.Id == mediaId)
            .Select(e => (Guid?)e.Season.TvShowId)
            .FirstOrDefaultAsync();
        if (episodeShow.HasValue) return episodeShow.Value;

        var seasonShow = await context.Set<Season>().AsNoTracking()
            .Where(s => s.Id == mediaId)
            .Select(s => (Guid?)s.TvShowId)
            .FirstOrDefaultAsync();
        return seasonShow ?? mediaId;
    }

    public Task<TrackPreferences?> GetTrackPreferencesAsync(Guid profileId) =>
        context.Set<UserProfile>().AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new TrackPreferences(p.PreferredAudioLanguage, p.SubtitleMode, p.PreferredSubtitleLanguage, p.PreferHearingImpairedSubtitles))
            .FirstOrDefaultAsync();

    public Task<ProfileTitlePlaybackChoice?> GetRememberedChoiceAsync(Guid profileId, string deviceId, Guid titleId) =>
        context.ProfileTitlePlaybackChoices.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProfileId == profileId && c.DeviceId == deviceId && c.TitleId == titleId);

    public async Task SaveRememberedChoiceAsync(ProfileTitlePlaybackChoice choice)
    {
        var stored = await context.ProfileTitlePlaybackChoices
            .FirstOrDefaultAsync(c => c.ProfileId == choice.ProfileId && c.DeviceId == choice.DeviceId && c.TitleId == choice.TitleId);
        if (stored == null) context.ProfileTitlePlaybackChoices.Add(choice);
        else context.Entry(stored).CurrentValues.SetValues(choice);
        await context.SaveChangesAsync();
    }

    public Task ForgetRememberedChoiceAsync(Guid profileId, string deviceId, Guid titleId) =>
        context.ProfileTitlePlaybackChoices
            .Where(c => c.ProfileId == profileId && c.DeviceId == deviceId && c.TitleId == titleId)
            .ExecuteDeleteAsync();

    public async Task SaveVideoTrackGeometryAsync(IReadOnlyCollection<VideoTrackGeometry> tracks)
    {
        foreach (var track in tracks)
        {
            await context.MediaVideoTracks
                .Where(t => t.Id == track.TrackId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.Width, track.Width)
                    .SetProperty(t => t.Height, track.Height)
                    .SetProperty(t => t.FrameRate, track.FrameRate)
                    .SetProperty(t => t.HdrType, track.HdrType));
        }
    }

    public Task<MediaStreamInfoDto?> GetMediaStreamInfoAsync(Guid mediaId) =>
        context.MediaItems
            .AsNoTracking()
            .AsSplitQuery()
            .Where(m => m.Id == mediaId)
            .Select(m => new MediaStreamInfoDto
            {
                Id = m.Id,
                Parts = m.MediaParts.Select(p => new MediaPartStreamInfoDto
                {
                    Id = p.Id,
                    FilePath = p.FilePath,
                    Resolution = p.Resolution,
                    Edition = p.Edition,
                    Container = p.Container,
                    OverallBitrate = p.OverallBitrate,
                    VideoTracks = p.VideoTracks.Select(vt => new TrackStreamInfoDto
                    {
                        Id = vt.Id,
                        Codec = vt.Codec,
                        IsDefault = vt.IsDefault,
                        HdrType = vt.HdrType,
                        BitDepth = vt.BitDepth,
                        Bitrate = vt.Bitrate,
                        Width = vt.Width,
                        Height = vt.Height,
                        FrameRate = vt.FrameRate,
                        StreamIndex = vt.StreamIndex
                    }).ToList(),
                    AudioTracks = p.AudioTracks.OrderBy(at => at.StreamIndex).Select(at => new TrackStreamInfoDto
                    {
                        Id = at.Id,
                        Title = at.Title,
                        Codec = at.Codec,
                        Language = at.Language,
                        IsDefault = at.IsDefault,
                        Channels = at.Channels,
                        StreamIndex = at.StreamIndex
                    }).ToList(),
                    SubtitleTracks = p.SubtitleTracks.Select(st => new SubtitleStreamInfoDto
                    {
                        Id = st.Id,
                        Codec = st.Codec,
                        Language = st.Language,
                        IsDefault = st.IsDefault,
                        IsForced = st.IsForced,
                        IsHearingImpaired = st.IsHearingImpaired
                    }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync();

    public async Task<List<NowPlayingSessionDto>> GetNowPlayingSessionsAsync(DateTime cutoffTime)
    {
        await StreamHistoryProjection.EndDeadSessionsAsync(context);

        return await context.StreamSessions
            .AsNoTracking()
            .Where(s => s.EndedAt == null && s.LastPingAt >= cutoffTime)
            .Select(s => new NowPlayingSessionDto
            {
                SessionId = s.Id,
                MediaId = s.MediaItemId,
                Title = s.MediaItem.Title,
                TvShowTitle = s.MediaItem is Episode ? ((Episode)s.MediaItem).Season.TvShow.Title : null,
                SeasonNumber = s.MediaItem is Episode ? (int?)((Episode)s.MediaItem).Season.SeasonNumber : null,
                EpisodeNumber = s.MediaItem is Episode ? (int?)((Episode)s.MediaItem).EpisodeNumber : null,
                PosterUrl = s.MediaItem.PosterUrl,
                DurationSeconds = s.MediaItem.Analysis != null && s.MediaItem.Analysis.Duration.HasValue
                    ? s.MediaItem.Analysis.Duration.Value.TotalSeconds
                    : 0,

                ClientName = s.ClientDevice.ClientName,
                DeviceName = s.ClientDevice.DeviceName,
                DeviceType = s.ClientDevice.DeviceType,
                IpAddress = s.IpAddress ?? s.ClientDevice.LastIpAddress,
                DeviceId = s.ClientDevice.DeviceId,

                Strategy = s.Strategy,
                VideoStrategy = s.VideoStrategy,
                AudioStrategy = s.AudioStrategy,
                SubtitleStrategy = s.SubtitleStrategy,

                Container = s.Container,
                VideoCodec = s.VideoCodec,
                AudioCodec = s.AudioCodec,
                TargetAudioChannels = s.TargetAudioChannels,
                Quality = s.Quality,
                BandwidthKbps = s.BandwidthKbps,
                Resolution = s.Resolution,
                HdrType = s.HdrType,
                OutputResolution = s.OutputResolution,
                OutputHdrType = s.OutputHdrType,
                DecisionLog = s.DecisionLog,

                CurrentPosition = s.CurrentPosition,
                IsPaused = s.IsPaused,

                UserName = s.UserProfile != null ? s.UserProfile.Name : "Unknown User",
                OriginalContainer = context.MediaParts.Where(p => p.Id == s.MediaPartId).Select(p => p.Container).FirstOrDefault(),
                OriginalVideoCodec = context.MediaVideoTracks.Where(t => t.Id == s.VideoTrackId).Select(t => t.Codec).FirstOrDefault(),
                OriginalAudioCodec = context.MediaAudioTracks.Where(t => t.Id == s.AudioTrackId).Select(t => t.Codec).FirstOrDefault(),
                OriginalAudioChannels = context.MediaAudioTracks.Where(t => t.Id == s.AudioTrackId).Select(t => t.Channels).FirstOrDefault(),
                OriginalSubtitleCodec = context.MediaSubtitleTracks.Where(t => t.Id == s.SubtitleTrackId).Select(t => t.Codec).FirstOrDefault()
            })
            .ToListAsync();
    }

    public Task<ClientDevice?> GetClientDeviceAsync(string deviceId) =>
        context.ClientDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);

    public async Task<StreamSession> CreateSessionAsync(StreamSession session)
    {
        context.StreamSessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    public Task<StreamSession?> GetSessionAsync(Guid sessionId) =>
        context.StreamSessions.FindAsync(sessionId).AsTask();

    public Task EndActiveSessionsForDeviceAsync(Guid clientDeviceId) =>
        context.StreamSessions
            .Where(s => s.ClientDeviceId == clientDeviceId && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, DateTime.UtcNow));

    public async Task UpdateSessionAsync(StreamSession session)
    {
        context.StreamSessions.Update(session);
        await context.SaveChangesAsync();
    }

    public async Task UpdateUserMediaStateAsync(Guid profileId, Guid mediaItemId, double currentPosition, double mediaDuration)
    {
        var isComplete = WatchStateTransition.IsComplete(currentPosition, mediaDuration);
        var isRestarted = WatchStateTransition.IsRestarted(currentPosition, mediaDuration);
        var resumePosition = WatchStateTransition.ResolveResumePosition(currentPosition, mediaDuration);

        var rowsAffected = await context.UserMediaStates
            .Where(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ResumePositionSeconds, resumePosition)
                // Watching a finished item again un-finishes it. The flag used to
                // be OR'd with its old value, which meant it could only ever go
                // true — so a re-watch stored a resume position that the details
                // page and Continue Watching both ignored.
                .SetProperty(x => x.IsPlayed, x => isComplete || (x.IsPlayed && !isRestarted))
                .SetProperty(x => x.LastPlayedAt, DateTime.UtcNow)
                .SetProperty(x => x.IsHiddenFromContinueWatching, false));

        if (rowsAffected > 0)
        {
            return;
        }

        context.UserMediaStates.Add(new UserMediaState
        {
            ProfileId = profileId,
            MediaItemId = mediaItemId,
            ResumePositionSeconds = resumePosition,
            IsPlayed = isComplete,
            LastPlayedAt = DateTime.UtcNow,
            IsHiddenFromContinueWatching = false
        });
        await context.SaveChangesAsync();
    }

    public Task<MediaExtra?> GetMediaExtraAsync(Guid extraId) =>
        context.MediaExtras.AsNoTracking().FirstOrDefaultAsync(e => e.Id == extraId);

    public Task<MediaStreamInfoDto?> GetExtraStreamInfoAsync(Guid extraId) =>
        context.MediaExtras
            .AsNoTracking()
            .AsSplitQuery()
            .Where(e => e.Id == extraId)
            .Select(e => new MediaStreamInfoDto
            {
                Id = e.Id,
                Parts = e.Parts.Select(p => new MediaPartStreamInfoDto
                {
                    Id = p.Id,
                    FilePath = p.FilePath,
                    Resolution = p.Resolution,
                    Container = p.Container,
                    OverallBitrate = p.OverallBitrate,
                    VideoTracks = p.VideoTracks.Select(vt => new TrackStreamInfoDto
                    {
                        Id = vt.Id,
                        Codec = vt.Codec,
                        IsDefault = vt.IsDefault,
                        HdrType = vt.HdrType,
                        BitDepth = vt.BitDepth,
                        Bitrate = vt.Bitrate,
                        Width = vt.Width,
                        Height = vt.Height,
                        FrameRate = vt.FrameRate,
                        StreamIndex = vt.StreamIndex
                    }).ToList(),
                    AudioTracks = p.AudioTracks.OrderBy(at => at.StreamIndex).Select(at => new TrackStreamInfoDto
                    {
                        Id = at.Id,
                        Title = at.Title,
                        Codec = at.Codec,
                        Language = at.Language,
                        IsDefault = at.IsDefault,
                        Channels = at.Channels,
                        StreamIndex = at.StreamIndex
                    }).ToList(),
                    SubtitleTracks = p.SubtitleTracks.Select(st => new SubtitleStreamInfoDto
                    {
                        Id = st.Id,
                        Codec = st.Codec,
                        Language = st.Language,
                        IsDefault = st.IsDefault,
                        IsForced = st.IsForced,
                        IsHearingImpaired = st.IsHearingImpaired
                    }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync();

    public async Task<MediaPart?> GetMediaPartForSessionAsync(Guid sessionId)
    {
        var session = await context.StreamSessions.FindAsync(sessionId);
        if (session == null)
        {
            return null;
        }

        // Resolve the actual selected part by session.MediaPartId — falling
        // back to the first-by-MediaItemId only when MediaPartId is empty
        // (e.g. legacy sessions). Include the track lists so the play
        // handler can look up StreamIndex by Track Guid for FFmpeg's `-map`
        // flags. Without this, FFmpeg's auto-selection grabbed the file's
        // default-flagged stream regardless of which one the user picked
        // in the Quality panel.
        // Split, because three sibling collections in one statement is a
        // cartesian product: a part with 1 video, 8 audio and 20 subtitle
        // tracks returns 160 rows to carry 29, and this runs on every play.
        var query = context.MediaParts
            .Include(p => p.VideoTracks)
            .Include(p => p.AudioTracks)
            .Include(p => p.SubtitleTracks)
            .AsSplitQuery()
            .AsQueryable();

        if (session.MediaPartId != Guid.Empty)
        {
            return await query.FirstOrDefaultAsync(p => p.Id == session.MediaPartId);
        }
        return await query.FirstOrDefaultAsync(p => p.MediaItemId == session.MediaItemId);
    }

    public Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(StreamHistoryQuery query) =>
        StreamHistoryProjection.LoadAsync(context, query);

    public async Task<IEnumerable<T>> GetProjectedActiveStreamsAsync<T>(TimeSpan activeThreshold, Expression<Func<StreamSession, T>> projection)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(activeThreshold);

        return await context.StreamSessions
            .AsNoTracking()
            .Where(s => s.EndedAt == null && s.LastPingAt >= cutoffTime)
            .Select(projection)
            .ToListAsync();
    }
}
