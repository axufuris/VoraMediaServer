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
    Task<IptvPlaylistVM> AddPlaylistAsync(string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, Vora.Domain.Enums.IptvChannelKind defaultKind, string? countryFilter = null, bool enableHealthCheck = false);
    Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, bool isActive, Vora.Domain.Enums.IptvChannelKind defaultKind, string? countryFilter = null, bool enableHealthCheck = false);
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
    Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId, LiveVideoLimits? limits);
    Task StopTimeshiftSessionAsync(Guid profileId);
    void PingTimeshiftSession(Guid profileId);
}

public class IptvManager : IIptvManager
{
    public const string HttpClientName = "IptvHttpClient";

    private const string DefaultTranscodeDirectory = "/transcode";
    private const string TimeshiftSubdirectory = TimeshiftCoordinator.TimeshiftSubdirectory;
    private const string PlaylistFileName = "index.m3u8";
    private const string SegmentFilePattern = "seg_%03d.ts";
    private const string FirstSegmentFileName = "seg_000.ts";
    private const string SecondSegmentFileName = "seg_001.ts";
    private const string TimeshiftTokenScope = "timeshift";
    private const int InitializationRetryDelayMs = 500;
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TimeshiftTokenTtl = TimeSpan.FromHours(4);

    private readonly IIptvRepository _repository;
    private readonly IIptvEpgService _epgService;
    private readonly IUserManager _userManager;
    private readonly ITaskQueueManager _taskQueue;
    private readonly ITimeshiftCoordinator _timeshiftCoordinator;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStreamingTokenSigner _tokenSigner;
    private readonly ITimeshiftVideo _timeshiftVideo;
    private readonly ILogger<IptvManager> _logger;

    public IptvManager(
        IIptvRepository repository,
        IIptvEpgService epgService,
        IUserManager userManager,
        ITaskQueueManager taskQueue,
        ITimeshiftCoordinator timeshiftCoordinator,
        ITunerRegistry tunerRegistry,
        IHttpClientFactory httpClientFactory,
        IStreamingTokenSigner tokenSigner,
        ITimeshiftVideo timeshiftVideo,
        ILogger<IptvManager> logger)
    {
        _repository = repository;
        _epgService = epgService;
        _userManager = userManager;
        _taskQueue = taskQueue;
        _timeshiftCoordinator = timeshiftCoordinator;
        _tunerRegistry = tunerRegistry;
        _httpClientFactory = httpClientFactory;
        _tokenSigner = tokenSigner;
        _timeshiftVideo = timeshiftVideo;
        _logger = logger;
    }

    public static string TimeshiftLeaseKey(Guid profileId) => $"timeshift:{profileId}";

    public async Task<List<IptvPlaylistVM>> GetAllPlaylistsAsync(Vora.Domain.Enums.IptvChannelKind? kind = null)
    {
        var playlists = await _repository.GetAllPlaylistsAsync(kind);
        return playlists.Select(p => MapToViewModel(p)).ToList();
    }

    public async Task<IptvPlaylistVM> AddPlaylistAsync(string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, Vora.Domain.Enums.IptvChannelKind defaultKind, string? countryFilter = null, bool enableHealthCheck = false)
    {
        var playlist = new IptvPlaylist
        {
            Name = name,
            M3uUrl = m3uUrl,
            IsActive = true,
            SupportsWebPlayback = supportsWebPlayback,
            DefaultChannelKind = defaultKind,
            CountryFilter = NormalizeCountryFilter(countryFilter),
            EnableHealthCheck = enableHealthCheck,
            TunerProfile = new IptvTunerProfile
            {
                MaxConcurrentStreams = maxConcurrentStreams
            }
        };

        await _repository.AddPlaylistAsync(playlist);
        await SyncM3uChannelsAsync(playlist);

        _taskQueue.QueueIptvEpgSync();
        if (playlist.EnableHealthCheck)
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
        }

        return MapToViewModel(playlist);
    }

    public async Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, string name, string m3uUrl, bool supportsWebPlayback, int maxConcurrentStreams, bool isActive, Vora.Domain.Enums.IptvChannelKind defaultKind, string? countryFilter = null, bool enableHealthCheck = false)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(id)
            ?? throw new InvalidOperationException("Playlist not found.");

        var m3uChanged = !string.Equals(playlist.M3uUrl, m3uUrl, StringComparison.Ordinal);
        var defaultKindChanged = playlist.DefaultChannelKind != defaultKind;
        var healthCheckTurnedOn = enableHealthCheck && !playlist.EnableHealthCheck;

        playlist.Name = name;
        playlist.M3uUrl = m3uUrl;
        playlist.SupportsWebPlayback = supportsWebPlayback;
        playlist.IsActive = isActive;
        playlist.DefaultChannelKind = defaultKind;
        playlist.CountryFilter = NormalizeCountryFilter(countryFilter);
        playlist.EnableHealthCheck = enableHealthCheck;

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

        var channelsResynced = m3uChanged || defaultKindChanged;
        if (channelsResynced)
        {
            await SyncM3uChannelsAsync(playlist);
            _taskQueue.QueueIptvEpgSync();
        }

        // Re-check when channels changed OR the admin just switched health checks
        // on (so the existing channels get their first probe without a re-sync).
        if (enableHealthCheck && (channelsResynced || healthCheckTurnedOn))
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
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
        if (playlist.EnableHealthCheck)
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
        }
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

        return allowed.Select(p => MapToViewModel(p, forClient: true)).ToList();
    }

    public Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime) =>
        _epgService.GetFilteredGuideAsync(userId, profileId, requestedChannelIds, startTime, endTime);

    public Task ToggleChannelVisibilityAsync(Guid channelId) =>
        _repository.ToggleChannelVisibilityAsync(channelId);

    public Task SetChannelKindAsync(Guid channelId, Vora.Domain.Enums.IptvChannelKind kind) =>
        _repository.SetChannelKindAsync(channelId, kind);

    public async Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId, LiveVideoLimits? limits)
    {
        var channel = await _repository.GetChannelByIdAsync(channelId);
        if (channel == null) throw new InvalidOperationException("Channel not found.");

        await using var profileLock = await _timeshiftCoordinator.LockProfileAsync(profileId);

        var leaseKey = TimeshiftLeaseKey(profileId);
        var tunerProfile = await _repository.GetTunerProfileByPlaylistIdAsync(channel.PlaylistId);
        var maxConcurrent = tunerProfile?.MaxConcurrentStreams ?? 0;

        _tunerRegistry.Release(leaseKey);
        if (!_tunerRegistry.TryAcquire(channel.PlaylistId, maxConcurrent, leaseKey, TunerLeaseKind.Timeshift))
        {
            return null;
        }

        try
        {
            var clientLimits = limits is { IsEmpty: false } ? limits : null;
            var known = StoredVideoFormat(channel);
            var encode = clientLimits is not null && known is not null && !LiveVideoDecision.Plays(known, clientLimits)
                ? LiveVideoDecision.EncodeFor(known, clientLimits)
                : null;
            var video = encode is null ? TimeshiftVideoArguments.Copy : await _timeshiftVideo.EncodeArgumentsAsync(encode, onCpu: false);
            if (video.Encodes && known is not null && clientLimits is not null)
            {
                _logger.LogInformation("Timeshift of {Channel} encodes with {Encoder}: {Reason}.",
                    channel.Name, video.Encoder, LiveVideoDecision.Mismatch(known, clientLimits));
            }

            var run = await LaunchTimeshiftAsync(profileId, channel.StreamUrl, video);
            if (run is null)
            {
                _tunerRegistry.Release(leaseKey);
                return null;
            }

            if (!video.Encodes)
            {
                var seen = await LearnVideoFormatAsync(channel, known, profileId, run);
                if (encode is null && clientLimits is not null && seen is not null && LiveVideoDecision.Mismatch(seen, clientLimits) is { } reason)
                {
                    encode = LiveVideoDecision.EncodeFor(seen, clientLimits);
                    video = await _timeshiftVideo.EncodeArgumentsAsync(encode, onCpu: false);
                    if (video.Encodes)
                    {
                        _logger.LogInformation("Timeshift of {Channel} restarts to encode with {Encoder}: {Reason}.", channel.Name, video.Encoder, reason);
                        run = await LaunchTimeshiftAsync(profileId, channel.StreamUrl, video);
                        if (run is null)
                        {
                            _tunerRegistry.Release(leaseKey);
                            return null;
                        }
                    }
                }
            }

            await WaitForFileAsync(profileId, run, SecondSegmentFileName);

            if (encode is not null && video.OnGpu && HasExited(run.Process) && !File.Exists(run.PlaylistPath))
            {
                _logger.LogWarning("Timeshift of {Channel} failed to encode with {Encoder}; retrying on the CPU.", channel.Name, video.Encoder);
                video = await _timeshiftVideo.EncodeArgumentsAsync(encode, onCpu: true);
                run = await LaunchTimeshiftAsync(profileId, channel.StreamUrl, video);
                if (run is null)
                {
                    _tunerRegistry.Release(leaseKey);
                    return null;
                }
                await WaitForFileAsync(profileId, run, SecondSegmentFileName);
            }

            if (!_timeshiftCoordinator.IsActive(profileId))
            {
                _tunerRegistry.Release(leaseKey);
                return null;
            }

            if (!File.Exists(run.PlaylistPath))
            {
                await _timeshiftCoordinator.StopAsync(profileId);
                _tunerRegistry.Release(leaseKey);
                throw new InvalidOperationException("FFmpeg failed to generate the stream playlist in time. The stream source may be dead or incompatible.");
            }

            var sessionId = Path.GetFileName(run.SessionPath);
            var token = _tokenSigner.Sign(TimeshiftTokenScope, $"{profileId}:{sessionId}", TimeshiftTokenTtl);
            return $"/api/streaming/hls/timeshift/{token}/{profileId}/{sessionId}/{PlaylistFileName}";
        }
        catch
        {
            _tunerRegistry.Release(leaseKey);
            throw;
        }
    }

    public async Task StopTimeshiftSessionAsync(Guid profileId)
    {
        await using var profileLock = await _timeshiftCoordinator.LockProfileAsync(profileId);
        _tunerRegistry.Release(TimeshiftLeaseKey(profileId));
        await _timeshiftCoordinator.StopAsync(profileId);
    }

    public void PingTimeshiftSession(Guid profileId) =>
        _timeshiftCoordinator.Heartbeat(profileId);

    internal static LiveVideoFormat? StoredVideoFormat(IptvChannel channel) =>
        channel.VideoCodec is { } codec
            ? new LiveVideoFormat(codec, channel.VideoProfile, channel.VideoLevel, channel.VideoHeight, channel.VideoFrameRate, channel.VideoInterlaced ?? false)
            : null;

    private sealed record TimeshiftRun(Process Process, string SessionPath, string PlaylistPath, DateTime ReadyBy);

    private async Task<TimeshiftRun?> LaunchTimeshiftAsync(Guid profileId, string streamUrl, TimeshiftVideoArguments video)
    {
        var sessionPath = await PrepareTimeshiftDirectoryAsync(profileId);
        var outputPath = Path.Combine(sessionPath, PlaylistFileName).Replace("\\", "/");
        var segmentPath = Path.Combine(sessionPath, SegmentFilePattern).Replace("\\", "/");

        var process = BuildTimeshiftProcess(streamUrl, outputPath, segmentPath, video);

        await _timeshiftCoordinator.StopAsync(profileId);
        if (!_timeshiftCoordinator.TryRegister(profileId, process, sessionPath))
        {
            process.Dispose();
            TryDeleteDirectory(sessionPath);
            return null;
        }
        process.Start();
        return new TimeshiftRun(process, sessionPath, outputPath, DateTime.UtcNow + InitializationTimeout);
    }

    private async Task<LiveVideoFormat?> LearnVideoFormatAsync(IptvChannel channel, LiveVideoFormat? known, Guid profileId, TimeshiftRun run)
    {
        if (!await WaitForFileAsync(profileId, run, FirstSegmentFileName)) return null;

        var seen = await _timeshiftVideo.ReadFormatAsync(Path.Combine(run.SessionPath, FirstSegmentFileName));
        if (seen is null || seen == known) return seen;

        try
        {
            await _repository.UpdateChannelVideoFormatAsync(channel.Id, seen, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the video format of {Channel}.", channel.Name);
        }
        return seen;
    }

    private async Task<bool> WaitForFileAsync(Guid profileId, TimeshiftRun run, string fileName)
    {
        var path = Path.Combine(run.SessionPath, fileName);
        while (!File.Exists(path))
        {
            if (DateTime.UtcNow >= run.ReadyBy || !_timeshiftCoordinator.IsActive(profileId) || HasExited(run.Process)) return File.Exists(path);
            await Task.Delay(InitializationRetryDelayMs);
        }
        return true;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
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

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete timeshift session folder {Folder}.", path);
        }
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

    internal static Process BuildTimeshiftProcess(string streamUrl, string outputPath, string segmentPath, TimeshiftVideoArguments video)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var args = process.StartInfo.ArgumentList;
        foreach (var argument in video.BeforeInput) args.Add(argument);
        args.Add("-reconnect"); args.Add("1");
        args.Add("-reconnect_streamed"); args.Add("1");
        args.Add("-reconnect_delay_max"); args.Add("5");
        args.Add("-i"); args.Add(streamUrl);
        foreach (var argument in video.Video) args.Add(argument);
        args.Add("-c:a"); args.Add("aac");
        args.Add("-b:a"); args.Add("128k");
        args.Add("-f"); args.Add("hls");
        args.Add("-hls_time"); args.Add("4");
        args.Add("-hls_list_size"); args.Add("0");
        args.Add("-hls_flags"); args.Add("append_list+temp_file");
        args.Add("-hls_segment_filename"); args.Add(segmentPath);
        args.Add(outputPath);

        return process;
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

    private static string? NormalizeCountryFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase) ? null : trimmed.ToUpperInvariant();
    }

    // forClient scopes the projection to what a viewer may see: the playlist's
    // CountryFilter is applied and channels that failed their last health check
    // are dropped entirely. Admin views leave it off so channel management sees
    // everything (including unhealthy channels, with their status).
    private static IptvPlaylistVM MapToViewModel(IptvPlaylist entity, bool forClient = false)
    {
        var channels = entity.Channels?.AsEnumerable() ?? Enumerable.Empty<IptvChannel>();
        if (forClient)
        {
            if (!string.IsNullOrWhiteSpace(entity.CountryFilter))
            {
                channels = channels.Where(c => string.Equals(c.CountryCode, entity.CountryFilter, StringComparison.OrdinalIgnoreCase));
            }
            // Unchecked (null) channels stay visible; only a confirmed failure hides one.
            channels = channels.Where(c => c.IsHealthy != false);
        }

        return new()
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
            CountryFilter = entity.CountryFilter,
            EnableHealthCheck = entity.EnableHealthCheck,
            Channels = channels.Select(c => IptvChannelVM.FromEntity(c, entity.Name)).ToList()
        };
    }

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
