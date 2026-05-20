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
                    Resolution = p.Resolution,
                    Container = p.Container,
                    OverallBitrate = p.OverallBitrate,
                    VideoTracks = p.VideoTracks.Select(vt => new TrackStreamInfoDto
                    {
                        Id = vt.Id,
                        Codec = vt.Codec,
                        IsDefault = vt.IsDefault,
                        HdrType = vt.HdrType
                    }).ToList(),
                    AudioTracks = p.AudioTracks.Select(at => new TrackStreamInfoDto
                    {
                        Id = at.Id,
                        Title = at.Title,
                        Codec = at.Codec,
                        IsDefault = at.IsDefault,
                        Channels = at.Channels
                    }).ToList(),
                    SubtitleTracks = p.SubtitleTracks.Select(st => new SubtitleStreamInfoDto
                    {
                        Id = st.Id,
                        Codec = st.Codec,
                        IsDefault = st.IsDefault,
                        IsForced = st.IsForced
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
                IpAddress = s.ClientDevice.LastIpAddress,
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
        var isPlayed = mediaDuration > 0 && (currentPosition / mediaDuration) >= 0.90;

        var rowsAffected = await context.UserMediaStates
            .Where(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ResumePositionSeconds, isPlayed ? 0 : currentPosition)
                .SetProperty(x => x.IsPlayed, x => x.IsPlayed || isPlayed)
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
            ResumePositionSeconds = isPlayed ? 0 : currentPosition,
            IsPlayed = isPlayed,
            LastPlayedAt = DateTime.UtcNow,
            IsHiddenFromContinueWatching = false
        });
        await context.SaveChangesAsync();
    }

    public async Task<MediaPart?> GetMediaPartForSessionAsync(Guid sessionId)
    {
        var session = await context.StreamSessions.FindAsync(sessionId);
        if (session == null)
        {
            return null;
        }

        return await context.MediaParts.FirstOrDefaultAsync(p => p.MediaItemId == session.MediaItemId);
    }

    public Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(int page, int pageSize, string search) =>
        StreamHistoryProjection.LoadAsync(context, page, pageSize, search);

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
