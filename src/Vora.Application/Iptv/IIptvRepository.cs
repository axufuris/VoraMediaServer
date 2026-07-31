using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IIptvRepository
{
    Task<List<IptvPlaylist>> GetActivePlaylistsAsync(CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetActiveChannelIdsAsync(CancellationToken cancellationToken = default);
    Task<List<IptvPlaylist>> GetAllPlaylistsAsync(IptvChannelKind? kind = null);
    Task<IptvPlaylist?> GetPlaylistByIdAsync(Guid id);
    Task<List<IptvChannel>> GetActiveChannelsAsync(CancellationToken cancellationToken);
    Task AddPlaylistAsync(IptvPlaylist playlist);
    Task UpdatePlaylistAsync(IptvPlaylist playlist);
    Task DeletePlaylistAsync(Guid id);
    Task UpdateChannelsAsync(Guid playlistId, List<IptvChannel> newChannels);
    Task<List<(Guid Id, string StreamUrl)>> GetChannelStreamsForPlaylistAsync(Guid playlistId);
    Task UpdateChannelHealthAsync(IReadOnlyDictionary<Guid, bool> results, DateTime checkedAt);
    Task ToggleChannelVisibilityAsync(Guid channelId);
    Task SetChannelKindAsync(Guid channelId, IptvChannelKind kind);

    Task<List<IptvEpgSource>> GetActiveEpgSourcesAsync(CancellationToken cancellationToken = default);
    Task<List<IptvEpgSource>> GetAllEpgSourcesAsync();
    Task<IptvEpgSource?> GetEpgSourceByIdAsync(Guid id);
    Task AddEpgSourceAsync(IptvEpgSource source);
    Task UpdateEpgSourceAsync(IptvEpgSource source);
    Task DeleteEpgSourceAsync(Guid id);

    Task<IptvRecordingSchedule> CreateRecordingScheduleAsync(IptvRecordingSchedule schedule);
    Task<IptvTunerProfile?> GetTunerProfileByPlaylistIdAsync(Guid playlistId);
    Task<int> GetActiveRecordingCountForPlaylistAsync(Guid playlistId);
    Task<List<IptvRecordingSession>> GetPendingSessionsOverlappingAsync(Guid playlistId, DateTime windowStart, DateTime windowEnd);
    Task MarkSessionFailedAsync(Guid sessionId, string reason);
    Task UpdateSessionStatusAsync(Guid sessionId, IptvRecordingSessionStatus status, string? outputPath = null, string? errorMessage = null);
    Task<IptvRecordingSession?> GetSessionByIdAsync(Guid sessionId);
    Task<List<IptvRecordingSession>> GetPendingSessionsToStartAsync(DateTime checkTime);
    Task<List<IptvRecordingSession>> GetActiveSessionsToStopAsync(DateTime checkTime);
    Task<List<IptvRecordingSchedule>> GetAllActiveSchedulesAsync();
    Task<bool> SessionExistsForProgramAsync(Guid scheduleId, string externalProgramId);
    Task CreateRecordingSessionAsync(IptvRecordingSession session);
    Task<List<IptvRecordingSession>> GetSessionsForProfileAsync(Guid profileId);
    Task<User> GetUserWithQuotaAsync(Guid userId);
    Task<long> GetDvrUsageBytesAsync(Guid userId);
    Task<long> GetDvrTotalUsageBytesAsync();
    Task<IptvRecordingSchedule?> GetScheduleWithSessionsAsync(Guid scheduleId);
    Task DeleteSessionAsync(Guid sessionId);
    Task<IptvChannel?> GetChannelByIdAsync(Guid id);
    Task<ServerSetting> GetServerSettingsAsync();
    Task<UserProfile?> GetUserProfileAsync(Guid profileId);
    Task<List<IptvRecordingSession>> GetCompletedRawSessionsAsync();
    Task UpdateSessionAsync(IptvRecordingSession session);
    Task DisableScheduleAsync(Guid scheduleId);
}
