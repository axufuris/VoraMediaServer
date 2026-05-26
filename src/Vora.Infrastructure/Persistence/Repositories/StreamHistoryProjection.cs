using Microsoft.EntityFrameworkCore;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;

namespace Vora.Infrastructure.Persistence.Repositories;

internal static class StreamHistoryProjection
{
    private static readonly TimeSpan DeadSessionThreshold = TimeSpan.FromMinutes(5);

    public static Task EndDeadSessionsAsync(VoraDbContext context)
    {
        var deadCutoff = DateTime.UtcNow.Subtract(DeadSessionThreshold);
        return context.StreamSessions
            .Where(s => s.EndedAt == null && s.LastPingAt < deadCutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, x => x.LastPingAt));
    }

    public static async Task<(List<HistorySessionDto> Data, int Total)> LoadAsync(VoraDbContext context, int page, int pageSize, string search)
    {
        var baseQuery = BuildHistoryBaseQuery(context, search);

        var keysQuery = baseQuery
            .Select(s => new HistoryGroupKey
            {
                Date = s.StartedAt.Date,
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice != null ? s.ClientDevice.DeviceId : null,
                MediaItemId = s.MediaItemId
            })
            .Distinct();

        var totalCount = await keysQuery.CountAsync();

        var pagedKeys = await keysQuery
            .OrderByDescending(k => k.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        if (pagedKeys.Count == 0)
        {
            return (new List<HistorySessionDto>(), 0);
        }

        var rawData = await LoadRawSessionDataAsync(baseQuery, pagedKeys);

        var grouped = rawData
            .GroupBy(s => new HistoryGroupKey
            {
                Date = s.StartedAt.Date,
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice?.DeviceId,
                MediaItemId = s.MediaItemId
            })
            .Select(g => BuildHistoryDto(g.OrderByDescending(x => x.StartedAt).ToList()))
            .OrderByDescending(x => DateTime.Parse(x.StartedAt))
            .ToList();

        return (grouped, totalCount);
    }

    private static IQueryable<StreamSession> BuildHistoryBaseQuery(VoraDbContext context, string search)
    {
        var query = context.StreamSessions.AsNoTracking();

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var searchPattern = $"%{search}%";
        return query.Where(s =>
            EF.Functions.ILike(s.MediaItem.Title, searchPattern)
            || (s.ClientDevice != null && EF.Functions.ILike(s.ClientDevice.DeviceName, searchPattern))
            || (s.UserProfile != null && EF.Functions.ILike(s.UserProfile.Name, searchPattern)));
    }

    private static async Task<List<StreamSession>> LoadRawSessionDataAsync(IQueryable<StreamSession> baseQuery, List<HistoryGroupKey> pagedKeys)
    {
        var minDate = pagedKeys.Min(k => k.Date);
        var maxDate = pagedKeys.Max(k => k.Date).AddDays(1);
        var mediaIds = pagedKeys.Select(k => k.MediaItemId).Distinct().ToList();

        var rawData = await baseQuery
            .AsSplitQuery()
            .Include(s => s.MediaItem).ThenInclude(m => m.Analysis)
            .Include(s => s.MediaItem.Library)
            .Include(s => s.ClientDevice)
            .Include(s => s.UserProfile)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.AudioTracks)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.SubtitleTracks)
            .Where(s => s.StartedAt >= minDate && s.StartedAt < maxDate && mediaIds.Contains(s.MediaItemId))
            .ToListAsync();

        var keySet = pagedKeys.ToHashSet();
        return rawData
            .Where(s => keySet.Contains(new HistoryGroupKey
            {
                Date = s.StartedAt.Date,
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice?.DeviceId,
                MediaItemId = s.MediaItemId
            }))
            .ToList();
    }

    private static HistorySessionDto BuildHistoryDto(List<StreamSession> sessions)
    {
        var newest = sessions[0];
        var oldest = sessions[^1];
        var userName = newest.UserProfile?.Name ?? "Unknown User";
        var libName = newest.MediaItem?.Library?.Name ?? "Unknown";
        var mediaDurationSecs = newest.MediaItem?.Analysis?.Duration?.TotalSeconds ?? 0;

        var totalPausedMins = (int)Math.Round(sessions.Sum(x => x.TotalPausedDuration / 60.0));
        var totalDurationMins = (int)Math.Round(
            sessions.Sum(x => (x.EndedAt ?? DateTime.UtcNow).Subtract(x.StartedAt).TotalMinutes) - totalPausedMins);
        var percentComplete = mediaDurationSecs > 0
            ? (int)Math.Clamp((newest.CurrentPosition / mediaDurationSecs) * 100, 0, 100)
            : 0;

        var activePart = newest.MediaItem?.MediaParts.FirstOrDefault();
        var dto = BuildHistoryRow(newest, userName, libName, activePart);
        dto.PausedMinutes = totalPausedMins;
        dto.DurationMinutes = totalDurationMins;
        dto.PercentComplete = percentComplete;
        dto.StartedAt = oldest.StartedAt.ToString("o");
        dto.IsGrouped = sessions.Count > 1;
        dto.SubSessions = sessions.Count > 1
            ? sessions.Select(sub => BuildSubSession(sub, userName, libName, activePart, mediaDurationSecs)).ToList()
            : null;

        return dto;
    }

    private static HistorySessionDto BuildSubSession(StreamSession sub, string userName, string libName, MediaPart? activePart, double mediaDurationSecs)
    {
        var dto = BuildHistoryRow(sub, userName, libName, activePart);
        dto.PausedMinutes = (int)Math.Round(sub.TotalPausedDuration / 60.0);
        dto.DurationMinutes = (int)Math.Round((sub.EndedAt ?? DateTime.UtcNow).Subtract(sub.StartedAt).TotalMinutes - (sub.TotalPausedDuration / 60.0));
        dto.PercentComplete = mediaDurationSecs > 0
            ? (int)Math.Clamp((sub.CurrentPosition / mediaDurationSecs) * 100, 0, 100)
            : 0;
        dto.StartedAt = sub.StartedAt.ToString("o");
        dto.IsGrouped = false;
        return dto;
    }

    private static HistorySessionDto BuildHistoryRow(StreamSession session, string userName, string libName, MediaPart? activePart)
    {
        var originalAudioTrack = activePart?.AudioTracks.FirstOrDefault(t => t.Id == session.AudioTrackId);

        return new HistorySessionDto
        {
            Id = session.Id.ToString(),
            Date = session.StartedAt.ToString("o"),
            UserName = userName,
            IpAddress = session.ClientDevice?.LastIpAddress ?? "Unknown",
            Platform = session.ClientDevice?.ClientName ?? "Unknown",
            Product = "Vora",
            Player = session.ClientDevice?.DeviceName ?? "Unknown",
            Title = session.MediaItem?.Title ?? "Unknown",
            MediaType = session.MediaItem is Episode || session.MediaItem is Season || session.MediaItem is TvShow ? "TvShow" : "Movie",
            LibraryId = session.MediaItem?.LibraryId ?? Guid.Empty,
            LibraryName = libName,

            Strategy = session.Strategy ?? "DirectPlay",
            VideoStrategy = session.VideoStrategy ?? "DirectPlay",
            AudioStrategy = session.AudioStrategy ?? "DirectPlay",

            VideoCodec = session.VideoCodec,
            OriginalVideoCodec = activePart?.VideoTracks.FirstOrDefault(t => t.Id == session.VideoTrackId)?.Codec,

            AudioCodec = session.AudioCodec,
            OriginalAudioCodec = originalAudioTrack?.Codec,
            OriginalAudioChannels = originalAudioTrack?.Channels,
            TargetAudioChannels = session.TargetAudioChannels,
            BandwidthKbps = session.BandwidthKbps,
            DecisionLog = session.DecisionLog,

            SubtitleStrategy = session.SubtitleStrategy ?? "None",
            OriginalSubtitleCodec = activePart?.SubtitleTracks.FirstOrDefault(t => t.Id == session.SubtitleTrackId)?.Codec,

            StoppedAt = (session.EndedAt ?? DateTime.UtcNow).ToString("o")
        };
    }

    private sealed class HistoryGroupKey : IEquatable<HistoryGroupKey>
    {
        public DateTime Date { get; set; }
        public Guid? ProfileId { get; set; }
        public string? DeviceId { get; set; }
        public Guid MediaItemId { get; set; }

        public bool Equals(HistoryGroupKey? other) => other != null
            && Date == other.Date
            && ProfileId == other.ProfileId
            && DeviceId == other.DeviceId
            && MediaItemId == other.MediaItemId;

        public override bool Equals(object? obj) => Equals(obj as HistoryGroupKey);

        public override int GetHashCode() => HashCode.Combine(Date, ProfileId, DeviceId, MediaItemId);
    }
}
