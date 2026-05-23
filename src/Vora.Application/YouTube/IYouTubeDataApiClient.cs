using Vora.Application.YouTube.Dtos;

namespace Vora.Application.YouTube;

public interface IYouTubeDataApiClient
{
    Task<bool> IsConfiguredAsync();
    Task<List<YouTubeVideoDto>> GetTrendingAsync(string regionCode, YouTubeSafeSearchLevel safeSearch, CancellationToken ct = default);
    Task<List<YouTubeVideoDto>> SearchAsync(string query, YouTubeSafeSearchLevel safeSearch, int maxResults = 20, CancellationToken ct = default);
    Task<YouTubeSearchPageDto> SearchPageAsync(string query, YouTubeSafeSearchLevel safeSearch, int maxResults = 20, string? pageToken = null, CancellationToken ct = default);
    Task<List<YouTubeVideoDto>> GetVideosByIdAsync(IEnumerable<string> videoIds, CancellationToken ct = default);
    Task<YouTubeChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default);
    Task<List<YouTubePlaylistDto>> GetChannelPlaylistsAsync(string channelId, int maxResults = 25, CancellationToken ct = default);
    Task<List<YouTubeVideoDto>> GetChannelRecentUploadsAsync(string channelId, int maxResults = 15, CancellationToken ct = default);
    Task<YouTubeSearchPageDto> GetChannelUploadsPageAsync(string channelId, string? pageToken = null, int maxResults = 50, CancellationToken ct = default);
    Task<List<YouTubeVideoDto>> GetRelatedVideosAsync(string videoId, YouTubeSafeSearchLevel safeSearch, int maxResults = 10, CancellationToken ct = default);
}
