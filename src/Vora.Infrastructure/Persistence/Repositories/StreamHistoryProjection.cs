using Microsoft.EntityFrameworkCore;
using Vora.Application.Streaming;
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

    public static async Task<(List<HistorySessionDto> Data, int Total)> LoadAsync(VoraDbContext context, StreamHistoryQuery query)
    {
        var zoneId = PostgresZoneId(query.TimeZone);
        var baseQuery = BuildHistoryBaseQuery(context, query);

        var groupsQuery = baseQuery
            .GroupBy(s => new
            {
                Date = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(s.StartedAt, zoneId).Date,
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice != null ? s.ClientDevice.DeviceId : null,
                s.MediaItemId
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.ProfileId,
                g.Key.DeviceId,
                g.Key.MediaItemId,
                Latest = g.Max(s => s.StartedAt)
            });

        var totalCount = await groupsQuery.CountAsync();

        var pagedGroups = await groupsQuery
            .OrderByDescending(g => g.Latest)
            .ThenBy(g => g.MediaItemId)
            .ThenBy(g => g.ProfileId)
            .ThenBy(g => g.DeviceId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        if (pagedGroups.Count == 0)
        {
            return (new List<HistorySessionDto>(), totalCount);
        }

        var pagedKeys = pagedGroups
            .Select(g => new HistoryGroupKey { Date = g.Date, ProfileId = g.ProfileId, DeviceId = g.DeviceId, MediaItemId = g.MediaItemId })
            .ToList();

        var rawData = await LoadRawSessionDataAsync(baseQuery, pagedKeys, query.TimeZone);

        // For episodes, resolve the parent show title + season/episode number so the
        // history can show "Show — S01E02 — Episode" rather than the episode title
        // alone. Fetched in one query keyed by episode id.
        var episodeIds = rawData
            .Where(s => s.MediaItem is Episode)
            .Select(s => s.MediaItemId)
            .Distinct()
            .ToList();

        var showInfo = episodeIds.Count == 0
            ? new Dictionary<Guid, EpisodeShowInfo>()
            : await context.Set<Episode>()
                .AsNoTracking()
                .Where(e => episodeIds.Contains(e.Id))
                .Select(e => new EpisodeShowInfo
                {
                    EpisodeId = e.Id,
                    ShowTitle = e.Season.TvShow.Title,
                    SeasonNumber = e.Season.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber
                })
                .ToDictionaryAsync(x => x.EpisodeId);

        var grouped = rawData
            .GroupBy(s => new HistoryGroupKey
            {
                Date = LocalDate(s.StartedAt, query.TimeZone),
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice?.DeviceId,
                MediaItemId = s.MediaItemId
            })
            .Select(g => g.OrderByDescending(x => x.StartedAt).ToList())
            .OrderByDescending(sessions => sessions[0].StartedAt)
            .ThenBy(sessions => sessions[0].MediaItemId)
            .Select(sessions => BuildHistoryDto(sessions, showInfo))
            .ToList();

        return (grouped, totalCount);
    }

    private static readonly string[] DirectStreamStrategies = ["Copy", "Remux", "DirectStream"];

    private static IQueryable<StreamSession> BuildHistoryBaseQuery(VoraDbContext context, StreamHistoryQuery filter)
    {
        var query = context.StreamSessions.AsNoTracking();

        if (filter.ProfileId is { } profileId)
        {
            query = query.Where(s => s.UserProfileId == profileId);
        }

        if (filter.LibraryIds.Count > 0)
        {
            var libraryIds = filter.LibraryIds.ToList();
            query = query.Where(s => libraryIds.Contains(s.MediaItem.LibraryId));
        }

        if (filter.Deliveries.Count > 0)
        {
            var directPlay = filter.Deliveries.Contains(HistoryDelivery.DirectPlay);
            var directStream = filter.Deliveries.Contains(HistoryDelivery.DirectStream);
            var transcode = filter.Deliveries.Contains(HistoryDelivery.Transcode);
            query = query.Where(s =>
                (directPlay && s.Strategy == "DirectPlay")
                || (directStream && DirectStreamStrategies.Contains(s.Strategy))
                || (transcode && s.Strategy == "Transcode"));
        }

        if (string.IsNullOrWhiteSpace(filter.Search))
        {
            return query;
        }

        var searchPattern = $"%{filter.Search}%";
        return query.Where(s =>
            EF.Functions.ILike(s.MediaItem.Title, searchPattern)
            || (s.ClientDevice != null && EF.Functions.ILike(s.ClientDevice.DeviceName, searchPattern))
            || (s.UserProfile != null && EF.Functions.ILike(s.UserProfile.Name, searchPattern))
            || EF.Functions.ILike(s.User.DisplayName, searchPattern));
    }

    private static string PostgresZoneId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) ? ianaId : "UTC";
    }

    private static DateTime LocalDate(DateTime startedAtUtc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc), zone).Date;

    private static async Task<List<StreamSession>> LoadRawSessionDataAsync(IQueryable<StreamSession> baseQuery, List<HistoryGroupKey> pagedKeys, TimeZoneInfo zone)
    {
        var from = DateTime.SpecifyKind(pagedKeys.Min(k => k.Date).AddDays(-1), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(pagedKeys.Max(k => k.Date).AddDays(2), DateTimeKind.Utc);
        var mediaIds = pagedKeys.Select(k => k.MediaItemId).Distinct().ToList();

        var rawData = await baseQuery
            .AsSplitQuery()
            .Include(s => s.MediaItem).ThenInclude(m => m.Analysis)
            .Include(s => s.MediaItem.Library)
            .Include(s => s.ClientDevice)
            .Include(s => s.UserProfile)
            .Include(s => s.User)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.AudioTracks)
            .Include(s => s.MediaItem.MediaParts).ThenInclude(p => p.SubtitleTracks)
            .Where(s => s.StartedAt >= from && s.StartedAt < to && mediaIds.Contains(s.MediaItemId))
            .ToListAsync();

        var keySet = pagedKeys.ToHashSet();
        return rawData
            .Where(s => keySet.Contains(new HistoryGroupKey
            {
                Date = LocalDate(s.StartedAt, zone),
                ProfileId = s.UserProfileId,
                DeviceId = s.ClientDevice?.DeviceId,
                MediaItemId = s.MediaItemId
            }))
            .ToList();
    }

    private sealed class EpisodeShowInfo
    {
        public Guid EpisodeId { get; set; }
        public string ShowTitle { get; set; } = string.Empty;
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
    }

    private static HistorySessionDto BuildHistoryDto(List<StreamSession> sessions, IReadOnlyDictionary<Guid, EpisodeShowInfo> showInfo)
    {
        var newest = sessions[0];
        var oldest = sessions[^1];
        var userName = newest.UserProfile?.Name ?? newest.User?.DisplayName ?? "Unknown user";
        var accountName = newest.User?.DisplayName;
        var libName = newest.MediaItem?.Library?.Name ?? "Unknown";
        showInfo.TryGetValue(newest.MediaItemId, out var episodeInfo);

        var dto = BuildHistoryRow(newest, userName, accountName, libName, episodeInfo);
        dto.PausedMinutes = (int)Math.Round(sessions.Sum(x => x.TotalPausedDuration) / 60.0);
        dto.DurationMinutes = (int)Math.Round(sessions.Sum(WatchedSeconds) / 60.0);
        dto.PercentComplete = sessions.Max(PercentComplete);
        dto.Date = oldest.StartedAt.ToString("o");
        dto.StartedAt = oldest.StartedAt.ToString("o");
        dto.IsGrouped = sessions.Count > 1;
        dto.SubSessions = sessions.Count > 1
            ? sessions.Select(sub => BuildSubSession(sub, userName, accountName, libName, episodeInfo)).ToList()
            : null;

        return dto;
    }

    private static HistorySessionDto BuildSubSession(StreamSession sub, string userName, string? accountName, string libName, EpisodeShowInfo? episodeInfo)
    {
        var dto = BuildHistoryRow(sub, userName, accountName, libName, episodeInfo);
        dto.PausedMinutes = (int)Math.Round(sub.TotalPausedDuration / 60.0);
        dto.DurationMinutes = (int)Math.Round(WatchedSeconds(sub) / 60.0);
        dto.PercentComplete = PercentComplete(sub);
        dto.StartedAt = sub.StartedAt.ToString("o");
        dto.IsGrouped = false;
        return dto;
    }

    private static double WatchedSeconds(StreamSession session) =>
        WatchProgress.WatchedSeconds(session.StartedAt, session.EndedAt ?? DateTime.UtcNow, session.TotalPausedDuration);

    private static int PercentComplete(StreamSession session) =>
        WatchProgress.PercentComplete(session.StartPosition, session.CurrentPosition, WatchedSeconds(session), MediaDurationSeconds(session));

    private static double MediaDurationSeconds(StreamSession session) =>
        (PlayedPart(session)?.Duration ?? session.MediaItem?.Analysis?.Duration)?.TotalSeconds ?? 0;

    private static MediaPart? PlayedPart(StreamSession session) =>
        session.MediaItem?.MediaParts.FirstOrDefault(p => p.Id == session.MediaPartId)
        ?? session.MediaItem?.MediaParts.FirstOrDefault();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static HistorySessionDto BuildHistoryRow(StreamSession session, string userName, string? accountName, string libName, EpisodeShowInfo? episodeInfo)
    {
        var activePart = PlayedPart(session);
        var originalAudioTrack = activePart?.AudioTracks.FirstOrDefault(t => t.Id == session.AudioTrackId);

        return new HistorySessionDto
        {
            Id = session.Id.ToString(),
            Date = session.StartedAt.ToString("o"),
            UserName = userName,
            AccountName = accountName,
            IpAddress = session.IpAddress ?? session.ClientDevice?.LastIpAddress ?? "Unknown",
            Location = NullIfBlank(session.IpAddress != null ? session.Location : session.ClientDevice?.Location),
            Platform = session.ClientDevice?.ClientName ?? "Unknown",
            Product = "Vora",
            Player = session.ClientDevice?.DeviceName ?? "Unknown",
            Title = session.MediaItem?.Title ?? "Unknown",
            ShowTitle = episodeInfo?.ShowTitle,
            SeasonNumber = episodeInfo?.SeasonNumber,
            EpisodeNumber = episodeInfo?.EpisodeNumber,
            MediaType = session.MediaItem?.BelongsToATvShow() == true ? "TvShow" : "Movie",
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
            SourceResolution = session.Resolution,
            SourceHdrType = session.HdrType,
            OutputResolution = session.OutputResolution,
            OutputHdrType = session.OutputHdrType,
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
