using Microsoft.EntityFrameworkCore;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;

namespace Vora.Infrastructure.Persistence.Repositories;

internal static class UserPlayHistoryProjection
{
    public static async Task<(List<UserProfileHistoryDto> Data, int Total)> LoadAsync(
        VoraDbContext context,
        Guid userId,
        Guid? profileId,
        int page,
        int pageSize,
        string search,
        string typeFilter)
    {
        var query = BuildHistoryBaseQuery(context, userId, profileId, search, typeFilter);

        var keysQuery = query
            .Select(x => new HistoryGroupKey
            {
                Date = x.Session.StartedAt.Date,
                ProfileId = x.Session.UserProfileId,
                DeviceId = x.Session.ClientDeviceId,
                MediaItemId = x.Session.MediaItemId
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
            return (new List<UserProfileHistoryDto>(), 0);
        }

        var rawData = await LoadRawSessionDataAsync(query, pagedKeys);
        var profiles = await LoadProfileNamesAsync(context, userId);
        var episodeMetadata = await LoadEpisodeMetadataAsync(context, rawData);

        var grouped = rawData
            .GroupBy(x => new HistoryGroupKey
            {
                Date = x.Session.StartedAt.Date,
                ProfileId = x.Session.UserProfileId,
                DeviceId = x.Session.ClientDeviceId,
                MediaItemId = x.Session.MediaItemId
            })
            .Select(g => BuildHistoryDto(g.ToList(), profiles, episodeMetadata))
            .OrderByDescending(g => DateTime.Parse(g.TimeStarted))
            .ToList();

        return (grouped, totalCount);
    }

    private static IQueryable<SessionMediaPair> BuildHistoryBaseQuery(VoraDbContext context, Guid userId, Guid? profileId, string search, string typeFilter)
    {
        var query = context.StreamSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Join(
                context.MediaItems.AsNoTracking().Include(m => m.Analysis),
                s => s.MediaItemId,
                m => m.Id,
                (s, m) => new SessionMediaPair { Session = s, Media = m });

        if (profileId.HasValue && profileId.Value != Guid.Empty)
        {
            query = query.Where(x => x.Session.UserProfileId == profileId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(x => x.Media.Title.ToLower().Contains(searchLower));
        }

        if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "All")
        {
            query = typeFilter switch
            {
                "Movies" => query.Where(x => x.Media is Movie),
                "TV Shows" => query.Where(x => x.Media is TvShow || x.Media is Season || x.Media is Episode),
                _ => query
            };
        }

        return query;
    }

    private static async Task<List<SessionMediaPair>> LoadRawSessionDataAsync(IQueryable<SessionMediaPair> query, List<HistoryGroupKey> pagedKeys)
    {
        var minDate = pagedKeys.Min(k => k.Date);
        var maxDate = pagedKeys.Max(k => k.Date).AddDays(1);
        var mediaItemIds = pagedKeys.Select(k => k.MediaItemId).Distinct().ToList();

        var rawData = await query
            .Where(x => x.Session.StartedAt >= minDate
                && x.Session.StartedAt < maxDate
                && mediaItemIds.Contains(x.Session.MediaItemId))
            .ToListAsync();

        var keyLookup = pagedKeys.ToHashSet();
        return rawData
            .Where(x => keyLookup.Contains(new HistoryGroupKey
            {
                Date = x.Session.StartedAt.Date,
                ProfileId = x.Session.UserProfileId,
                DeviceId = x.Session.ClientDeviceId,
                MediaItemId = x.Session.MediaItemId
            }))
            .ToList();
    }

    private static Task<Dictionary<Guid, string>> LoadProfileNamesAsync(VoraDbContext context, Guid userId) =>
        context.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Id, p => p.Name);

    private static async Task<Dictionary<Guid, EpisodeMetadata>> LoadEpisodeMetadataAsync(VoraDbContext context, List<SessionMediaPair> rawData)
    {
        var episodeIds = rawData
            .Where(x => x.Media is Episode)
            .Select(x => x.Media.Id)
            .Distinct()
            .ToList();

        if (episodeIds.Count == 0)
        {
            return new Dictionary<Guid, EpisodeMetadata>();
        }

        return await context.Set<Episode>()
            .AsNoTracking()
            .Include(e => e.Season).ThenInclude(s => s.TvShow)
            .Where(e => episodeIds.Contains(e.Id))
            .ToDictionaryAsync(
                e => e.Id,
                e => new EpisodeMetadata(e.Season.TvShow.Title, e.Season.SeasonNumber, e.EpisodeNumber, e.Season.TvShow.ContentRating ?? string.Empty));
    }

    private static UserProfileHistoryDto BuildHistoryDto(
        List<SessionMediaPair> sessions,
        Dictionary<Guid, string> profiles,
        Dictionary<Guid, EpisodeMetadata> episodeMetadata)
    {
        var ordered = sessions.OrderByDescending(x => x.Session.StartedAt).ToList();
        var newest = ordered[0];
        var oldest = ordered[^1];

        var totalPausedMins = (int)Math.Round(ordered.Sum(x => x.Session.TotalPausedDuration / 60.0));
        var totalDurationMins = (int)Math.Round(
            ordered.Sum(x => (x.Session.EndedAt ?? DateTime.UtcNow).Subtract(x.Session.StartedAt).TotalMinutes) - totalPausedMins);

        var profileName = ResolveProfileName(newest.Session.UserProfileId, profiles);
        var dto = BuildHistoryRow(newest, profileName, episodeMetadata);
        dto.DurationMinutes = totalDurationMins;
        dto.PausedMinutes = totalPausedMins;
        dto.TimeStarted = oldest.Session.StartedAt.ToString("o");
        dto.TimeStopped = (newest.Session.EndedAt ?? DateTime.UtcNow).ToString("o");
        dto.IsGrouped = ordered.Count > 1;
        dto.SubSessions = ordered.Count > 1
            ? ordered.Select(s => BuildSubSession(s, profileName, episodeMetadata)).ToList()
            : null;

        return dto;
    }

    private static UserProfileHistoryDto BuildSubSession(SessionMediaPair pair, string profileName, Dictionary<Guid, EpisodeMetadata> episodeMetadata)
    {
        var dto = BuildHistoryRow(pair, profileName, episodeMetadata);
        dto.DurationMinutes = (int)Math.Round((pair.Session.EndedAt ?? DateTime.UtcNow).Subtract(pair.Session.StartedAt).TotalMinutes - (pair.Session.TotalPausedDuration / 60.0));
        dto.PausedMinutes = (int)Math.Round(pair.Session.TotalPausedDuration / 60.0);
        dto.TimeStarted = pair.Session.StartedAt.ToString("o");
        dto.TimeStopped = (pair.Session.EndedAt ?? DateTime.UtcNow).ToString("o");
        dto.IsGrouped = false;
        return dto;
    }

    private static UserProfileHistoryDto BuildHistoryRow(SessionMediaPair pair, string profileName, Dictionary<Guid, EpisodeMetadata> episodeMetadata)
    {
        var isEpisode = pair.Media is Episode;
        EpisodeMetadata? meta = isEpisode && episodeMetadata.TryGetValue(pair.Media.Id, out var m) ? m : null;

        return new UserProfileHistoryDto
        {
            SessionId = pair.Session.Id,
            Title = pair.Media.Title,
            TvShowTitle = meta?.ShowTitle,
            SeasonNumber = meta?.SeasonNumber,
            EpisodeNumber = meta?.EpisodeNumber,
            ReleaseYear = pair.Media.ReleaseDate?.Year,
            Type = pair.Media is Movie ? "Movie" : isEpisode ? "Episode" : "Unknown",
            ContentRating = meta != null && !string.IsNullOrEmpty(meta.ContentRating) ? meta.ContentRating : pair.Media.ContentRating,
            ProfileId = pair.Session.UserProfileId ?? Guid.Empty,
            ProfileName = profileName
        };
    }

    private static string ResolveProfileName(Guid? profileId, Dictionary<Guid, string> profiles) =>
        profileId.HasValue && profiles.TryGetValue(profileId.Value, out var name) ? name : "Unknown User";

    private sealed class SessionMediaPair
    {
        public StreamSession Session { get; set; } = null!;
        public MediaItem Media { get; set; } = null!;
    }

    private sealed class HistoryGroupKey : IEquatable<HistoryGroupKey>
    {
        public DateTime Date { get; set; }
        public Guid? ProfileId { get; set; }
        public Guid DeviceId { get; set; }
        public Guid MediaItemId { get; set; }

        public bool Equals(HistoryGroupKey? other) => other != null
            && Date == other.Date
            && ProfileId == other.ProfileId
            && DeviceId == other.DeviceId
            && MediaItemId == other.MediaItemId;

        public override bool Equals(object? obj) => Equals(obj as HistoryGroupKey);

        public override int GetHashCode() => HashCode.Combine(Date, ProfileId, DeviceId, MediaItemId);
    }

    private sealed record EpisodeMetadata(string ShowTitle, int SeasonNumber, int EpisodeNumber, string ContentRating);
}
