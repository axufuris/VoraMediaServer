using System.Linq.Expressions;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Streaming;

public interface IStreamRepository
{
    Task<List<NowPlayingSessionDto>> GetNowPlayingSessionsAsync(DateTime cutoffTime);
    Task<MediaStreamInfoDto?> GetMediaStreamInfoAsync(Guid mediaId);
    Task<Guid> ResolvePlayableMediaIdAsync(Guid mediaId, Guid? profileId);
    Task<Guid> ResolveTitleIdAsync(Guid mediaId);
    Task<ProfileTitlePlaybackChoice?> GetRememberedChoiceAsync(Guid profileId, string deviceId, Guid titleId);
    Task<TrackPreferences?> GetTrackPreferencesAsync(Guid profileId);
    Task SaveRememberedChoiceAsync(ProfileTitlePlaybackChoice choice);
    Task ForgetRememberedChoiceAsync(Guid profileId, string deviceId, Guid titleId);
    Task<ClientDevice?> GetClientDeviceAsync(string deviceId);
    Task<StreamSession> CreateSessionAsync(StreamSession session);
    Task<StreamSession?> GetSessionAsync(Guid sessionId);
    Task EndActiveSessionsForDeviceAsync(Guid clientDeviceId);
    Task UpdateSessionAsync(StreamSession session);
    Task UpdateUserMediaStateAsync(Guid profileId, Guid mediaItemId, double currentPosition, double mediaDuration);
    Task<MediaPart?> GetMediaPartForSessionAsync(Guid sessionId);
    Task<MediaExtra?> GetMediaExtraAsync(Guid extraId);
    Task<MediaStreamInfoDto?> GetExtraStreamInfoAsync(Guid extraId);
    Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(StreamHistoryQuery query);
    Task<IEnumerable<T>> GetProjectedActiveStreamsAsync<T>(TimeSpan activeThreshold, Expression<Func<StreamSession, T>> projection);
}