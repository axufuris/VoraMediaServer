using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Iptv.ViewModels;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

public interface IIptvManager
{
    Task<List<IptvPlaylistVM>> GetAllPlaylistsAsync(Vora.Domain.Enums.IptvChannelKind? kind = null);
    Task<List<IptvPlaylistVM>> GetClientPlaylistsAsync(Guid userId, Guid? profileId = null);
    Task<IptvPlaylistVM> AddPlaylistAsync(string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, Vora.Domain.Enums.IptvChannelKind defaultKind);
    Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, bool isActive, Vora.Domain.Enums.IptvChannelKind defaultKind);
    Task DeletePlaylistAsync(Guid id);
    Task RefreshPlaylistAsync(Guid id);

    Task<List<IptvEpgSourceVM>> GetAllEpgSourcesAsync();
    Task<IptvEpgSourceVM> AddEpgSourceAsync(string name, string xmlTvUrl, int priority);
    Task<IptvEpgSourceVM> UpdateEpgSourceAsync(Guid id, string name, string xmlTvUrl, int priority, bool isActive);
    Task DeleteEpgSourceAsync(Guid id);
    Task RefreshEpgSourceAsync(Guid id);
    Task<IptvEpgDiagnosticsVM> GetEpgDiagnosticsAsync();

    Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime);
    Task ToggleChannelVisibilityAsync(Guid channelId);
    Task SetChannelKindAsync(Guid channelId, Vora.Domain.Enums.IptvChannelKind kind);
    Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId);
    Task StopTimeshiftSessionAsync(Guid profileId);
    void PingTimeshiftSession(Guid profileId);
}

public class IptvManager : IIptvManager
{
    public const string HttpClientName = "IptvHttpClient";

    private const string DefaultTranscodeDirectory = "/transcode";
    private const string TimeshiftSubdirectory = "timeshift";
    private const string PlaylistFileName = "index.m3u8";
    private const string SegmentFilePattern = "seg_%03d.ts";
    private const string SecondSegmentFileName = "seg_001.ts";
    private const string TimeshiftTokenScope = "timeshift";
    private const int InitializationRetryLimit = 40;
    private const int InitializationRetryDelayMs = 500;
    private static readonly TimeSpan TimeshiftTokenTtl = TimeSpan.FromHours(4);

    private readonly IIptvRepository _repository;
    private readonly IIptvEpgService _epgService;
    private readonly IUserManager _userManager;
    private readonly ITaskQueueManager _taskQueue;
    private readonly ITimeshiftCoordinator _timeshiftCoordinator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStreamingTokenSigner _tokenSigner;
    private readonly ILogger<IptvManager> _logger;

    public IptvManager(
        IIptvRepository repository,
        IIptvEpgService epgService,
        IUserManager userManager,
        ITaskQueueManager taskQueue,
        ITimeshiftCoordinator timeshiftCoordinator,
        IHttpClientFactory httpClientFactory,
        IStreamingTokenSigner tokenSigner,
        ILogger<IptvManager> logger)
    {
        _repository = repository;
        _epgService = epgService;
        _userManager = userManager;
        _taskQueue = taskQueue;
        _timeshiftCoordinator = timeshiftCoordinator;
        _httpClientFactory = httpClientFactory;
        _tokenSigner = tokenSigner;
        _logger = logger;
    }

    public async Task<List<IptvPlaylistVM>> GetAllPlaylistsAsync(Vora.Domain.Enums.IptvChannelKind? kind = null)
    {
        var playlists = await _repository.GetAllPlaylistsAsync(kind);
        return playlists.Select(MapToViewModel).ToList();
    }

    public async Task<IptvPlaylistVM> AddPlaylistAsync(string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, Vora.Domain.Enums.IptvChannelKind defaultKind)
    {
        var playlist = new IptvPlaylist
        {
            Name = name,
            M3uUrl = m3uUrl,
            IsActive = true,
            SupportsWebPlayback = supportsWebPlayback,
            DefaultChannelKind = defaultKind,
            TunerProfile = new IptvTunerProfile
            {
                MaxConcurrentStreams = maxConcurrentStreams
            }
        };

        await _repository.AddPlaylistAsync(playlist);
        await SyncM3uChannelsAsync(playlist);

        _taskQueue.QueueIptvEpgSync();

        return MapToViewModel(playlist);
    }

    public async Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, bool isActive, Vora.Domain.Enums.IptvChannelKind defaultKind)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(id)
            ?? throw new InvalidOperationException("Playlist not found.");

        var m3uChanged = !string.Equals(playlist.M3uUrl, m3uUrl, StringComparison.Ordinal);
        var defaultKindChanged = playlist.DefaultChannelKind != defaultKind;

        playlist.Name = name;
        playlist.M3uUrl = m3uUrl;
        playlist.SupportsWebPlayback = supportsWebPlayback;
        playlist.IsActive = isActive;
        playlist.DefaultChannelKind = defaultKind;

        if (playlist.TunerProfile == null)
        {
            playlist.TunerProfile = new IptvTunerProfile
            {
                PlaylistId = playlist.Id,
                MaxConcurrentStreams = maxConcurrentStreams
            };
        }
        else
        {
            playlist.TunerProfile.MaxConcurrentStreams = maxConcurrentStreams;
        }

        await _repository.UpdatePlaylistAsync(playlist);

        if (m3uChanged || defaultKindChanged)
        {
            await SyncM3uChannelsAsync(playlist);
            _taskQueue.QueueIptvEpgSync();
        }

        var refreshed = await _repository.GetPlaylistByIdAsync(playlist.Id) ?? playlist;
        return MapToViewModel(refreshed);
    }

    public async Task DeletePlaylistAsync(Guid id)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(id);
        if (playlist == null) return;

        var channelIds = playlist.Channels.Select(c => c.ExternalChannelId).ToList();
        await _repository.DeletePlaylistAsync(id);
        await _epgService.RemoveChannelsFromCacheAsync(channelIds);
    }

    public async Task RefreshPlaylistAsync(Guid id)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(id);
        if (playlist == null) return;

        await SyncM3uChannelsAsync(playlist);
        _taskQueue.QueueIptvEpgSync();
    }

    public async Task<List<IptvEpgSourceVM>> GetAllEpgSourcesAsync()
    {
        var sources = await _repository.GetAllEpgSourcesAsync();
        return sources.Select(MapToViewModel).ToList();
    }

    public async Task<IptvEpgSourceVM> AddEpgSourceAsync(string name, string xmlTvUrl, int priority)
    {
        var source = new IptvEpgSource
        {
            Name = name,
            XmlTvUrl = xmlTvUrl,
            Priority = priority,
            IsActive = true
        };

        await _repository.AddEpgSourceAsync(source);
        await _epgService.SyncEpgDataAsync(CancellationToken.None);

        var refreshed = await _repository.GetEpgSourceByIdAsync(source.Id) ?? source;
        return MapToViewModel(refreshed);
    }

    public async Task<IptvEpgSourceVM> UpdateEpgSourceAsync(Guid id, string name, string xmlTvUrl, int priority, bool isActive)
    {
        var source = await _repository.GetEpgSourceByIdAsync(id)
            ?? throw new InvalidOperationException("EPG source not found.");

        source.Name = name;
        source.XmlTvUrl = xmlTvUrl;
        source.Priority = priority;
        source.IsActive = isActive;

        await _repository.UpdateEpgSourceAsync(source);
        await _epgService.SyncEpgDataAsync(CancellationToken.None);

        var refreshed = await _repository.GetEpgSourceByIdAsync(source.Id) ?? source;
        return MapToViewModel(refreshed);
    }

    public async Task DeleteEpgSourceAsync(Guid id)
    {
        await _repository.DeleteEpgSourceAsync(id);
        await _epgService.SyncEpgDataAsync(CancellationToken.None);
    }

    public Task RefreshEpgSourceAsync(Guid id) => _epgService.SyncEpgDataAsync(CancellationToken.None);

    public async Task<IptvEpgDiagnosticsVM> GetEpgDiagnosticsAsync()
    {
        var playlists = await _repository.GetAllPlaylistsAsync();
        var sources = await _repository.GetAllEpgSourcesAsync();
        var allStats = _epgService.GetAllSyncStats();
        var coveredIds = _epgService.GetCoveredChannelIds();

        var allDbChannels = playlists
            .SelectMany(p => (p.Channels ?? new List<IptvChannel>()).Select(c => new DbChannelSample
            {
                ExternalChannelId = c.ExternalChannelId,
                Name = c.Name,
                PlaylistName = p.Name
            }))
            .ToList();

        var dbSample = allDbChannels.Take(25).ToList();

        var uncovered = allDbChannels
            .Where(c => !string.IsNullOrEmpty(c.ExternalChannelId) && !coveredIds.Contains(c.ExternalChannelId))
            .OrderBy(c => c.Name)
            .Take(30)
            .ToList();

        var channelsWithEpg = allDbChannels.Count(c => !string.IsNullOrEmpty(c.ExternalChannelId) && coveredIds.Contains(c.ExternalChannelId));

        var sourceDiags = sources
            .Select(s =>
            {
                allStats.TryGetValue(s.Id, out var stats);
                return new EpgSourceDiagnostics
                {
                    SourceId = s.Id,
                    Name = s.Name,
                    XmlTvUrl = s.XmlTvUrl,
                    TotalProgrammes = stats?.TotalProgrammes ?? 0,
                    MatchedProgrammes = stats?.MatchedProgrammes ?? 0,
                    MatchedChannels = stats?.MatchedChannels ?? 0,
                    MatchRate = stats == null || stats.TotalProgrammes == 0 ? 0 : (double)stats.MatchedProgrammes / stats.TotalProgrammes,
                    UnmatchedSamples = stats?.UnmatchedSamples ?? new List<string>(),
                    SyncedAt = stats?.SyncedAt ?? s.LastSyncedAt,
                    LastError = s.LastError
                };
            })
            .ToList();

        return new IptvEpgDiagnosticsVM
        {
            DbSampleIds = dbSample,
            Sources = sourceDiags,
            Coverage = new ChannelCoverageSummary
            {
                TotalChannels = allDbChannels.Count,
                ChannelsWithEpg = channelsWithEpg,
                CoverageRate = allDbChannels.Count == 0 ? 0 : (double)channelsWithEpg / allDbChannels.Count,
                UncoveredSamples = uncovered
            }
        };
    }

    public async Task<List<IptvPlaylistVM>> GetClientPlaylistsAsync(Guid userId, Guid? profileId = null)
    {
        var user = await _userManager.GetUserAccountAsync(userId);
        var allPlaylists = await _repository.GetAllPlaylistsAsync();
        var allowed = allPlaylists.AsEnumerable();

        if (user != null && !user.IsAdmin && !user.HasAllIptvAccess)
        {
            allowed = allowed.Where(p => user.AllowedIptvPlaylistIds.Contains(p.Id));
        }

        if (profileId.HasValue && user != null)
        {
            var profile = user.Profiles.FirstOrDefault(p => p.Id == profileId.Value);
            if (profile != null && !profile.IsAdmin && !profile.HasAllIptvAccess)
            {
                allowed = allowed.Where(p => profile.AllowedIptvPlaylistIds.Contains(p.Id));
            }
        }

        return allowed.Select(MapToViewModel).ToList();
    }

    public Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime) =>
        _epgService.GetFilteredGuideAsync(userId, profileId, requestedChannelIds, startTime, endTime);

    public Task ToggleChannelVisibilityAsync(Guid channelId) =>
        _repository.ToggleChannelVisibilityAsync(channelId);

    public Task SetChannelKindAsync(Guid channelId, Vora.Domain.Enums.IptvChannelKind kind) =>
        _repository.SetChannelKindAsync(channelId, kind);

    public async Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId)
    {
        var channel = await _repository.GetChannelByIdAsync(channelId);
        if (channel == null) throw new InvalidOperationException("Channel not found.");

        await EnsureTunerAvailableAsync(channel.PlaylistId);

        var sessionPath = await PrepareTimeshiftDirectoryAsync(profileId);
        var sessionId = Path.GetFileName(sessionPath);

        var outputPath = Path.Combine(sessionPath, PlaylistFileName).Replace("\\", "/");
        var segmentPath = Path.Combine(sessionPath, SegmentFilePattern).Replace("\\", "/");

        var process = BuildTimeshiftProcess(channel.StreamUrl, outputPath, segmentPath);

        await StopTimeshiftSessionAsync(profileId);
        _timeshiftCoordinator.TryRegister(profileId, process);
        process.Start();

        await WaitForBufferAsync(profileId, sessionPath);

        if (!_timeshiftCoordinator.IsActive(profileId))
        {
            return null;
        }

        if (!File.Exists(outputPath))
        {
            await StopTimeshiftSessionAsync(profileId);
            throw new InvalidOperationException("FFmpeg failed to generate the stream playlist in time. The stream source may be dead or incompatible.");
        }

        var token = _tokenSigner.Sign(TimeshiftTokenScope, $"{profileId}:{sessionId}", TimeshiftTokenTtl);
        return $"/api/streaming/hls/timeshift/{token}/{profileId}/{sessionId}/{PlaylistFileName}";
    }

    public Task StopTimeshiftSessionAsync(Guid profileId) =>
        _timeshiftCoordinator.StopAsync(profileId);

    public void PingTimeshiftSession(Guid profileId) =>
        _timeshiftCoordinator.Heartbeat(profileId);

    private async Task EnsureTunerAvailableAsync(Guid playlistId)
    {
        var tunerProfile = await _repository.GetTunerProfileByPlaylistIdAsync(playlistId);
        var activeCount = await _repository.GetActiveRecordingCountForPlaylistAsync(playlistId);

        if (tunerProfile != null && tunerProfile.MaxConcurrentStreams > 0 && activeCount >= tunerProfile.MaxConcurrentStreams)
        {
            throw new InvalidOperationException("No tuners available for this playlist.");
        }
    }

    private async Task<string> PrepareTimeshiftDirectoryAsync(Guid profileId)
    {
        var settings = await _repository.GetServerSettingsAsync();
        var tempDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? DefaultTranscodeDirectory : settings.TranscoderTempDirectory;

        var baseProfilePath = Path.Combine(tempDir, TimeshiftSubdirectory, profileId.ToString());
        Directory.CreateDirectory(baseProfilePath);

        CleanupPreviousSessions(baseProfilePath);

        var sessionId = Guid.NewGuid().ToString("N");
        var sessionPath = Path.Combine(baseProfilePath, sessionId);
        Directory.CreateDirectory(sessionPath);

        return sessionPath;
    }

    private void CleanupPreviousSessions(string baseProfilePath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(baseProfilePath))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to delete old timeshift session folder {Folder}.", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate old timeshift sessions in {Path}.", baseProfilePath);
        }
    }

    private static Process BuildTimeshiftProcess(string streamUrl, string outputPath, string segmentPath)
    {
        var args = new[]
        {
            "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5",
            $"-i \"{streamUrl}\"",
            "-c:v copy -c:a aac -b:a 128k -f hls -hls_time 4 -hls_list_size 0",
            "-hls_flags append_list+temp_file",
            $"-hls_segment_filename \"{segmentPath}\"",
            $"\"{outputPath}\""
        };

        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = string.Join(" ", args),
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private async Task WaitForBufferAsync(Guid profileId, string sessionPath)
    {
        var secondChunk = Path.Combine(sessionPath, SecondSegmentFileName);
        var retries = 0;

        while (!File.Exists(secondChunk) && retries < InitializationRetryLimit && _timeshiftCoordinator.IsActive(profileId))
        {
            await Task.Delay(InitializationRetryDelayMs);
            retries++;
        }
    }

    private async Task SyncM3uChannelsAsync(IptvPlaylist playlist)
    {
        if (string.IsNullOrWhiteSpace(playlist.M3uUrl)) return;

        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            List<IptvChannel> channels;

            if (IsRadioBrowserUrl(playlist.M3uUrl))
            {
                var jsonUrl = ConvertToRadioBrowserJsonUrl(playlist.M3uUrl);
                var json = await httpClient.GetStringAsync(jsonUrl);
                channels = RadioBrowserParser.Parse(json, playlist.Id, playlist.DefaultChannelKind);
            }
            else
            {
                var m3uContent = await httpClient.GetStringAsync(playlist.M3uUrl);
                channels = M3uChannelParser.Parse(m3uContent, playlist.Id, playlist.DefaultChannelKind);
            }

            await _repository.UpdateChannelsAsync(playlist.Id, channels);

            playlist.LastError = null;
            playlist.LastSyncedAt = DateTime.UtcNow;
            await _repository.UpdatePlaylistAsync(playlist);

            _logger.LogInformation("Successfully parsed and saved {ChannelCount} channels for {PlaylistName}.", channels.Count, playlist.Name);
        }
        catch (HttpRequestException ex)
        {
            playlist.LastError = $"HTTP {(int?)ex.StatusCode}: {ex.Message}";
            await _repository.UpdatePlaylistAsync(playlist);
            _logger.LogError(ex, "HTTP Error fetching M3U for {PlaylistName}.", playlist.Name);
        }
        catch (Exception ex)
        {
            playlist.LastError = ex.Message;
            await _repository.UpdatePlaylistAsync(playlist);
            _logger.LogError(ex, "Failed to download or parse M3U for {PlaylistName}.", playlist.Name);
        }
    }

    private static bool IsRadioBrowserUrl(string url) =>
        url.Contains("radio-browser.info", StringComparison.OrdinalIgnoreCase);

    private static string ConvertToRadioBrowserJsonUrl(string url) =>
        url.Replace("/m3u/", "/json/", StringComparison.OrdinalIgnoreCase);

    private static IptvPlaylistVM MapToViewModel(IptvPlaylist entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            M3uUrl = entity.M3uUrl,
            IsActive = entity.IsActive,
            SupportsWebPlayback = entity.SupportsWebPlayback,
            MaxConcurrentStreams = entity.TunerProfile?.MaxConcurrentStreams ?? 0,
            LastError = entity.LastError,
            LastSyncedAt = entity.LastSyncedAt,
            DefaultChannelKind = entity.DefaultChannelKind.ToString(),
            Channels = entity.Channels?.Select(c => new IptvChannelVM
            {
                Id = c.Id,
                PlaylistId = c.PlaylistId,
                ExternalChannelId = c.ExternalChannelId,
                Name = c.Name,
                LogoUrl = c.LogoUrl,
                GroupTitle = c.GroupTitle,
                StreamUrl = c.StreamUrl,
                Resolution = c.Resolution,
                CountryCode = c.CountryCode,
                IsHiddenByAdmin = c.IsHiddenByAdmin,
                Kind = c.Kind.ToString()
            }).ToList() ?? new List<IptvChannelVM>()
        };

    private static IptvEpgSourceVM MapToViewModel(IptvEpgSource entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            XmlTvUrl = entity.XmlTvUrl,
            Priority = entity.Priority,
            IsActive = entity.IsActive,
            LastError = entity.LastError,
            LastSyncedAt = entity.LastSyncedAt
        };
}
