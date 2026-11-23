using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Iptv.ViewModels;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public interface IIptvManager
{
    Task<List<IptvPlaylistVM>> GetAllPlaylistsAsync(IptvChannelKind? kind = null);
    Task<List<IptvPlaylistVM>> GetClientPlaylistsAsync(Guid userId, Guid? profileId = null);
    Task<IptvPlaylistVM> AddPlaylistAsync(IptvPlaylistSettingsDto settings);
    Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, IptvPlaylistSettingsDto settings);
    Task<IptvConnectionTestVM> TestConnectionAsync(IptvPlaylistSettingsDto settings, Guid? existingId, CancellationToken cancellationToken = default);
    Task DeletePlaylistAsync(Guid id);
    Task RefreshPlaylistAsync(Guid id);
    Task RefreshActivePlaylistChannelsAsync(CancellationToken cancellationToken = default);

    Task<List<IptvEpgSourceVM>> GetAllEpgSourcesAsync();
    Task<IptvEpgSourceVM> AddEpgSourceAsync(string name, string xmlTvUrl, int priority);
    Task<IptvEpgSourceVM> UpdateEpgSourceAsync(Guid id, string name, string xmlTvUrl, int priority, bool isActive);
    Task DeleteEpgSourceAsync(Guid id);
    Task RefreshEpgSourceAsync(Guid id);
    Task<IptvEpgDiagnosticsVM> GetEpgDiagnosticsAsync();

    Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime);
    Task ToggleChannelVisibilityAsync(Guid channelId);
    Task SetChannelKindAsync(Guid channelId, IptvChannelKind kind);
    Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId, LiveVideoLimits? limits);
    Task StopTimeshiftSessionAsync(Guid profileId);
    void PingTimeshiftSession(Guid profileId);
    Task<string> StartLiveRemuxAsync(IptvChannel channel, IptvUpstream upstream, Guid viewerKey, int connectionLimit, LiveVideoLimits? limits = null);
    Task StopLiveRemuxAsync(Guid viewerKey);
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
    private const string ProviderGuideSuffix = " (provider guide)";
    private const string DeviceGuideSuffix = " (HDHomeRun guide)";
    private const string DeviceGuideHost = "https://api.hdhomerun.com";
    // The live remux has no rewind, so its segments are short: the player gets its first two
    // after about four seconds of video instead of eight. Timeshift keeps four-second segments.
    public const int LiveRemuxSegmentSeconds = 2;
    public const int LiveRemuxWindowSegments = 10;
    private const int InitializationRetryDelayMs = 500;
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TimeshiftTokenTtl = TimeSpan.FromHours(4);

    private readonly IIptvRepository _repository;
    private readonly IIptvEpgService _epgService;
    private readonly IUserManager _userManager;
    private readonly ITaskQueueManager _taskQueue;
    private readonly ITimeshiftCoordinator _timeshiftCoordinator;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly IStreamingTokenSigner _tokenSigner;
    private readonly ITimeshiftVideo _timeshiftVideo;
    private readonly IEncodeSlots _encodeSlots;
    private readonly IIptvChannelLoader _channelLoader;
    private readonly IIptvUpstreamResolver _upstreamResolver;
    private readonly IIptvSecretProtector _secretProtector;
    private readonly ILogger<IptvManager> _logger;

    public IptvManager(
        IIptvRepository repository,
        IIptvEpgService epgService,
        IUserManager userManager,
        ITaskQueueManager taskQueue,
        ITimeshiftCoordinator timeshiftCoordinator,
        ITunerRegistry tunerRegistry,
        IStreamingTokenSigner tokenSigner,
        ITimeshiftVideo timeshiftVideo,
        IEncodeSlots encodeSlots,
        IIptvChannelLoader channelLoader,
        IIptvUpstreamResolver upstreamResolver,
        IIptvSecretProtector secretProtector,
        ILogger<IptvManager> logger)
    {
        _repository = repository;
        _epgService = epgService;
        _userManager = userManager;
        _taskQueue = taskQueue;
        _timeshiftCoordinator = timeshiftCoordinator;
        _tunerRegistry = tunerRegistry;
        _tokenSigner = tokenSigner;
        _timeshiftVideo = timeshiftVideo;
        _encodeSlots = encodeSlots;
        _channelLoader = channelLoader;
        _upstreamResolver = upstreamResolver;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public static string TimeshiftLeaseKey(Guid profileId) => $"timeshift:{profileId}";

    public async Task<List<IptvPlaylistVM>> GetAllPlaylistsAsync(IptvChannelKind? kind = null)
    {
        var playlists = await _repository.GetAllPlaylistsAsync(kind);
        return playlists.Select(IptvPlaylistView.ForAdmin).ToList();
    }

    public async Task<IptvPlaylistVM> AddPlaylistAsync(IptvPlaylistSettingsDto settings)
    {
        var playlist = new IptvPlaylist { Name = settings.Name };
        IptvPlaylistSettings.Apply(playlist, settings, _secretProtector);
        playlist.IsActive = true;

        await _repository.AddPlaylistAsync(playlist);
        await SyncChannelsAsync(playlist);

        _taskQueue.QueueIptvEpgSync();
        if (playlist.EnableHealthCheck)
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
        }

        return IptvPlaylistView.ForAdmin(playlist);
    }

    public async Task<IptvPlaylistVM> UpdatePlaylistAsync(Guid id, IptvPlaylistSettingsDto settings)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(id)
            ?? throw new InvalidOperationException("Playlist not found.");

        var healthCheckTurnedOn = settings.EnableHealthCheck && !playlist.EnableHealthCheck;
        var change = IptvPlaylistSettings.Apply(playlist, settings, _secretProtector);

        await _repository.UpdatePlaylistAsync(playlist);

        if (change.SourceChanged)
        {
            await SyncChannelsAsync(playlist);
        }

        if (change.SourceChanged || change.GuideChanged)
        {
            _taskQueue.QueueIptvEpgSync();
        }

        if (settings.EnableHealthCheck && (change.SourceChanged || healthCheckTurnedOn))
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
        }

        var refreshed = await _repository.GetPlaylistByIdAsync(playlist.Id) ?? playlist;
        return IptvPlaylistView.ForAdmin(refreshed);
    }

    public async Task<IptvConnectionTestVM> TestConnectionAsync(IptvPlaylistSettingsDto settings, Guid? existingId, CancellationToken cancellationToken = default)
    {
        var existing = existingId is { } id ? await _repository.GetPlaylistByIdAsync(id) : null;
        var candidate = new IptvPlaylist
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = settings.Name,
            SourceType = existing?.SourceType ?? IptvSourceType.M3u,
            M3uUrl = existing?.M3uUrl,
            ServerUrl = existing?.ServerUrl,
            Username = existing?.Username,
            PasswordCiphertext = existing?.PasswordCiphertext,
            UserAgent = existing?.UserAgent,
            Referer = existing?.Referer
        };

        string? password = null;
        try
        {
            IptvPlaylistSettings.Apply(candidate, settings, _secretProtector);
            password = _upstreamResolver.PasswordFor(candidate);

            var load = await _channelLoader.LoadAsync(candidate, cancellationToken);
            return TestResult(null, load.Account, load.Channels.Count, load.Device);
        }
        catch (XtreamAccountException ex)
        {
            return TestResult(ex.Message, ex.Account, 0, null);
        }
        catch (Exception ex) when (ex is IptvSourceException or ArgumentException)
        {
            return TestResult(IptvUrlRedactor.Redact(ex.Message, password, settings.NewPassword), null, 0, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Testing the IPTV source {Name} failed with {ErrorType}: {Error}", settings.Name, ex.GetType().Name, IptvUrlRedactor.Redact(ex.Message, password, settings.NewPassword));
            return TestResult("The source could not be read. The server log has the details.", null, 0, null);
        }
    }

    // A tuner reports through the account fields: its name and model as the status, its tuner count as the connections.
    private static IptvConnectionTestVM TestResult(string? error, XtreamAccountInfo? account, int channelCount, HdHomeRunDevice? device) => new()
    {
        Succeeded = error is null,
        Error = error,
        AccountStatus = device is null ? account?.Status : device.Model is null ? device.Name : $"{device.Name} ({device.Model})",
        AccountExpiresAt = account?.ExpiresAt,
        AccountMaxConnections = device is null ? account?.MaxConnections : device.TunerCount,
        ChannelCount = channelCount
    };

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

        await SyncChannelsAsync(playlist);
        _taskQueue.QueueIptvEpgSync();
        if (playlist.EnableHealthCheck)
        {
            _taskQueue.QueueIptvHealthCheck(playlist.Id, playlist.Name);
        }
    }

    public async Task RefreshActivePlaylistChannelsAsync(CancellationToken cancellationToken = default)
    {
        var active = await _repository.GetActivePlaylistsAsync(cancellationToken);
        foreach (var summary in active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playlist = await _repository.GetPlaylistByIdAsync(summary.Id);
            if (playlist is null || !playlist.IsActive) continue;
            await SyncChannelsAsync(playlist, cancellationToken);
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
                return Diagnostics(s.Id, s.Name, s.XmlTvUrl, stats, stats?.SyncedAt ?? s.LastSyncedAt, s.LastError);
            })
            .ToList();

        sourceDiags.AddRange(playlists
            .Where(p => p.SourceType == IptvSourceType.Xtream && p.UseProviderGuide)
            .Select(p =>
            {
                allStats.TryGetValue(p.Id, out var stats);
                return Diagnostics(p.Id, p.Name + ProviderGuideSuffix, IptvUrlRedactor.SchemeAndHost(p.ServerUrl) ?? string.Empty, stats, stats?.SyncedAt, stats?.Error);
            }));

        sourceDiags.AddRange(playlists
            .Where(p => p.SourceType == IptvSourceType.HdHomeRun && p.UseProviderGuide)
            .Select(p =>
            {
                allStats.TryGetValue(p.Id, out var stats);
                return Diagnostics(p.Id, p.Name + DeviceGuideSuffix, DeviceGuideHost, stats, stats?.SyncedAt, stats?.Error);
            }));

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

    private static EpgSourceDiagnostics Diagnostics(Guid id, string name, string address, IptvSourceSyncStats? stats, DateTime? syncedAt, string? lastError) => new()
    {
        SourceId = id,
        Name = name,
        XmlTvUrl = address,
        TotalProgrammes = stats?.TotalProgrammes ?? 0,
        MatchedProgrammes = stats?.MatchedProgrammes ?? 0,
        MatchedChannels = stats?.MatchedChannels ?? 0,
        MatchRate = stats == null || stats.TotalProgrammes == 0 ? 0 : (double)stats.MatchedProgrammes / stats.TotalProgrammes,
        UnmatchedSamples = stats?.UnmatchedSamples ?? new List<string>(),
        SyncedAt = syncedAt,
        LastError = lastError
    };

    public async Task<List<IptvPlaylistVM>> GetClientPlaylistsAsync(Guid userId, Guid? profileId = null)
    {
        var user = await _userManager.GetUserAccountAsync(userId);
        if (user is null) return new List<IptvPlaylistVM>();

        var profile = profileId is { } id ? user.Profiles.FirstOrDefault(p => p.Id == id) : null;
        var allPlaylists = await _repository.GetAllPlaylistsAsync();

        return allPlaylists
            .Where(p => IptvPlaylistAccess.Allows(user, profile, p.Id))
            .Select(IptvPlaylistView.ForClient)
            .ToList();
    }

    public Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime) =>
        _epgService.GetFilteredGuideAsync(userId, profileId, requestedChannelIds, startTime, endTime);

    public Task ToggleChannelVisibilityAsync(Guid channelId) =>
        _repository.ToggleChannelVisibilityAsync(channelId);

    public Task SetChannelKindAsync(Guid channelId, IptvChannelKind kind) =>
        _repository.SetChannelKindAsync(channelId, kind);

    public async Task<string?> StartTimeshiftSessionAsync(Guid channelId, Guid userId, Guid profileId, LiveVideoLimits? limits)
    {
        var channel = await _repository.GetChannelWithPlaylistAsync(channelId);
        if (channel == null) throw new InvalidOperationException("Channel not found.");

        if (!await IptvPlaylistAccess.AllowsAsync(_userManager, userId, profileId, channel.PlaylistId))
        {
            throw new UnauthorizedAccessException("This profile does not have access to the channel's playlist.");
        }

        IptvPlaylist? playlist = channel.Playlist;
        var upstream = _upstreamResolver.Resolve(playlist, channel);
        var secret = playlist is null ? null : _upstreamResolver.PasswordFor(playlist);

        await using var profileLock = await _timeshiftCoordinator.LockProfileAsync(profileId);

        _tunerRegistry.ReleaseStartingWith(IptvPassthroughService.ViewerLiveLeasePrefix(profileId, channel.PlaylistId));

        var maxConcurrent = playlist is null ? 0 : IptvConnectionLimit.Of(playlist);
        var result = await JoinOrStartAsync(channel, profileId, upstream, secret, limits, LiveSessionKind.Timeshift, maxConcurrent);
        if (result.Session is { } session) return SessionUrl(session);

        if (result.Failure == LiveStartFailure.StreamUnreadable)
        {
            throw new InvalidOperationException("FFmpeg failed to generate the stream playlist in time. The stream source may be dead or incompatible.");
        }
        return null;
    }

    public async Task StopTimeshiftSessionAsync(Guid profileId)
    {
        await using var profileLock = await _timeshiftCoordinator.LockProfileAsync(profileId);
        await LeaveAsync(profileId);
    }

    public void PingTimeshiftSession(Guid profileId) =>
        _timeshiftCoordinator.HeartbeatViewer(profileId);

    public async Task<string> StartLiveRemuxAsync(IptvChannel channel, IptvUpstream upstream, Guid viewerKey, int connectionLimit, LiveVideoLimits? limits = null)
    {
        IptvPlaylist? playlist = channel.Playlist;
        var secret = playlist is null ? null : _upstreamResolver.PasswordFor(playlist);

        await using var viewerLock = await _timeshiftCoordinator.LockProfileAsync(viewerKey);

        var result = await JoinOrStartAsync(channel, viewerKey, upstream, secret, limits, LiveSessionKind.Remux, connectionLimit);
        if (result.Session is not { } session)
        {
            throw result.Failure switch
            {
                LiveStartFailure.TunersBusy => new TunerLimitReachedException(),
                LiveStartFailure.Superseded => new InvalidOperationException("Another stream for this viewer is starting."),
                _ => new IptvSourceException("The channel's stream could not be read.")
            };
        }

        _logger.LogInformation("Passthrough of {Channel} is remuxed to HLS because its stream isn't HLS.", channel.Name);
        return SessionUrl(session);
    }

    public async Task StopLiveRemuxAsync(Guid viewerKey)
    {
        await using var viewerLock = await _timeshiftCoordinator.LockProfileAsync(viewerKey);
        if (_timeshiftCoordinator.KindOfViewer(viewerKey) != LiveSessionKind.Remux) return;

        await LeaveAsync(viewerKey);
    }

    private enum LiveStartFailure
    {
        TunersBusy,
        Superseded,
        Gone,
        StreamUnreadable
    }

    private sealed record LiveSessionStart(Guid SessionKey, string SessionPath);

    private sealed record LiveStartResult(LiveSessionStart? Session, LiveStartFailure Failure)
    {
        public static LiveStartResult Started(LiveSessionStart session) => new(session, default);

        public static LiveStartResult Failed(LiveStartFailure failure) => new(null, failure);
    }

    private string SessionUrl(LiveSessionStart session)
    {
        var sessionId = Path.GetFileName(session.SessionPath);
        var token = _tokenSigner.Sign(TimeshiftTokenScope, $"{session.SessionKey}:{sessionId}", TimeshiftTokenTtl);
        return $"/api/streaming/hls/timeshift/{token}/{session.SessionKey}/{sessionId}/{PlaylistFileName}";
    }

    private async Task LeaveAsync(Guid viewerKey)
    {
        if (await _timeshiftCoordinator.DetachAsync(viewerKey) is { } stopped)
        {
            _tunerRegistry.Release(TimeshiftLeaseKey(stopped));
        }
    }

    private async Task<LiveStartResult> JoinOrStartAsync(
        IptvChannel channel, Guid viewerKey, IptvUpstream upstream, string? secret, LiveVideoLimits? sent, LiveSessionKind kind, int maxConcurrent)
    {
        await using var channelLock = await _timeshiftCoordinator.LockChannelAsync(channel.Id, kind);

        foreach (var exited in await _timeshiftCoordinator.StopExitedAsync(channel.Id, kind))
        {
            _tunerRegistry.Release(TimeshiftLeaseKey(exited));
        }

        var limits = LiveVideoLimits.Effective(sent);
        if (await TryJoinAsync(channel, viewerKey, limits, kind) is { } joined) return LiveStartResult.Started(joined);

        await LeaveAsync(viewerKey);

        var sessionKey = Guid.NewGuid();
        var leaseKey = TimeshiftLeaseKey(sessionKey);
        if (!_tunerRegistry.TryAcquire(channel.PlaylistId, maxConcurrent, leaseKey, TunerLeaseKind.Timeshift))
        {
            return LiveStartResult.Failed(LiveStartFailure.TunersBusy);
        }

        try
        {
            var run = await StartFormatAwareAsync(channel, sessionKey, upstream, secret, sent, kind);
            if (run is null)
            {
                _tunerRegistry.Release(leaseKey);
                return LiveStartResult.Failed(LiveStartFailure.Superseded);
            }

            if (!_timeshiftCoordinator.IsActive(sessionKey))
            {
                _tunerRegistry.Release(leaseKey);
                return LiveStartResult.Failed(LiveStartFailure.Gone);
            }

            if (!File.Exists(run.PlaylistPath))
            {
                await _timeshiftCoordinator.StopAsync(sessionKey);
                _tunerRegistry.Release(leaseKey);
                return LiveStartResult.Failed(TunerIsBusy(run, channel) ? LiveStartFailure.TunersBusy : LiveStartFailure.StreamUnreadable);
            }

            if (!(await _timeshiftCoordinator.AttachAsync(viewerKey, sessionKey)).Attached)
            {
                _tunerRegistry.Release(leaseKey);
                return LiveStartResult.Failed(LiveStartFailure.Gone);
            }

            return LiveStartResult.Started(new LiveSessionStart(sessionKey, run.SessionPath));
        }
        catch
        {
            await _timeshiftCoordinator.StopAsync(sessionKey);
            _tunerRegistry.Release(leaseKey);
            throw;
        }
    }

    private async Task<LiveSessionStart?> TryJoinAsync(IptvChannel channel, Guid viewerKey, LiveVideoLimits limits, LiveSessionKind kind)
    {
        var joinableFor = LiveSessionLifetime.JoinableFor(await _repository.GetServerSettingsAsync());
        var now = DateTime.UtcNow;
        var own = _timeshiftCoordinator.SessionOfViewer(viewerKey);
        var candidate = _timeshiftCoordinator.SharedSessions(channel.Id, kind)
            .Where(session => session.SessionKey != own
                && now - session.StartedAtUtc < joinableFor
                && File.Exists(Path.Combine(session.SessionPath, PlaylistFileName))
                && LiveVideoDecision.Serves(session.Encode, session.Source, limits))
            .OrderBy(session => session.Encode is null ? 0 : 1)
            .ThenBy(session => session.Encode?.MaxHeight ?? int.MaxValue)
            .FirstOrDefault();
        if (candidate is null) return null;

        var attached = await _timeshiftCoordinator.AttachAsync(viewerKey, candidate.SessionKey);
        if (!attached.Attached) return null;

        if (attached.StoppedSessionKey is { } stopped) _tunerRegistry.Release(TimeshiftLeaseKey(stopped));

        _logger.LogInformation(
            "{Kind} of {Channel} joins a running session instead of starting another ({Encoder}, {Viewers} viewers now).",
            kind == LiveSessionKind.Remux ? "Live remux" : "Timeshift", channel.Name, candidate.Encode is null ? "copy" : "encode", candidate.Viewers + 1);
        return new LiveSessionStart(candidate.SessionKey, candidate.SessionPath);
    }

    internal static LiveVideoFormat? StoredVideoFormat(IptvChannel channel) =>
        channel.VideoCodec is { } codec
            ? new LiveVideoFormat(codec, channel.VideoProfile, channel.VideoLevel, channel.VideoHeight, channel.VideoFrameRate, channel.VideoInterlaced)
            : null;

    private sealed class RunDiagnostics
    {
        public volatile bool UpstreamBusy;
    }

    private sealed record TimeshiftRun(Process Process, string SessionPath, string PlaylistPath, DateTime ReadyBy, RunDiagnostics Diagnostics);

    // What ffmpeg prints when the upstream answers 503. A tuner answers that when every tuner is in use.
    internal static bool IsUpstreamBusy(string ffmpegLine) =>
        ffmpegLine.Contains("HTTP error 503", StringComparison.OrdinalIgnoreCase);

    private static bool TunerIsBusy(TimeshiftRun run, IptvChannel channel) =>
        run.Diagnostics.UpstreamBusy && channel.Playlist is { SourceType: IptvSourceType.HdHomeRun };

    /// <summary>
    /// Starts the ffmpeg session for a live channel and returns it once its second segment exists.
    /// The video is copied when the player can play it and encoded to H.264 (deinterlaced) when it
    /// can not. The channel's format is remembered, so only the first play of a channel whose format
    /// is not yet known starts by copying and restarts when its first segment shows the player
    /// can't take it. A player that sent no limits is assumed to take <see cref="LiveVideoLimits.Default"/>.
    /// </summary>
    private async Task<TimeshiftRun?> StartFormatAwareAsync(IptvChannel channel, Guid sessionKey, IptvUpstream upstream, string? secret, LiveVideoLimits? sent, LiveSessionKind kind)
    {
        var limits = LiveVideoLimits.Effective(sent);
        var segmentSeconds = kind == LiveSessionKind.Remux ? LiveRemuxSegmentSeconds : LiveVideoDecision.TimeshiftSegmentSeconds;
        var label = kind == LiveSessionKind.Remux ? "Live remux" : "Timeshift";

        var known = StoredVideoFormat(channel);
        var source = known;
        var encode = known is not null && !LiveVideoDecision.Plays(known, limits)
            ? LiveVideoDecision.EncodeFor(known, limits, segmentSeconds)
            : null;
        TimeshiftVideoArguments video;
        TimeshiftRun? run;
        if (encode is null)
        {
            video = TimeshiftVideoArguments.Copy;
            run = await LaunchTimeshiftAsync(sessionKey, channel.Id, null, source, upstream, secret, video, kind);
        }
        else
        {
            (video, run) = await LaunchEncodeAsync(encode, source, onCpu: false, launchCopy: true, channel.Id, sessionKey, upstream, secret, kind);
        }
        if (video.Encodes && known is not null)
        {
            _logger.LogInformation("{Kind} of {Channel} encodes with {Encoder}: {Reason}.",
                label, channel.Name, video.Encoder, LiveVideoDecision.Mismatch(known, limits));
        }
        if (run is null) return null;

        try
        {
            if (!video.Encodes)
            {
                var seen = await LearnVideoFormatAsync(channel, known, sessionKey, run);
                if (seen is not null)
                {
                    source = seen;
                    _timeshiftCoordinator.SetSource(sessionKey, seen);
                }

                if (encode is null && seen is not null && LiveVideoDecision.Mismatch(seen, limits) is { } reason)
                {
                    encode = LiveVideoDecision.EncodeFor(seen, limits, segmentSeconds);
                    var restart = await LaunchEncodeAsync(encode, source, onCpu: false, launchCopy: false, channel.Id, sessionKey, upstream, secret, kind);
                    video = restart.Video;
                    if (video.Encodes)
                    {
                        _logger.LogInformation("{Kind} of {Channel} restarts to encode with {Encoder}: {Reason}.", label, channel.Name, video.Encoder, reason);
                        run = restart.Run;
                        if (run is null) return null;
                    }
                }
            }

            await WaitForFileAsync(sessionKey, run, SecondSegmentFileName);

            if (encode is not null && video.OnGpu && HasExited(run.Process) && !File.Exists(run.PlaylistPath))
            {
                _logger.LogWarning("{Kind} of {Channel} failed to encode with {Encoder}; retrying on the CPU.", label, channel.Name, video.Encoder);
                (video, run) = await LaunchEncodeAsync(encode, source, onCpu: true, launchCopy: true, channel.Id, sessionKey, upstream, secret, kind);
                if (run is null) return null;
                await WaitForFileAsync(sessionKey, run, SecondSegmentFileName);
            }
        }
        catch (EncodeLimitReachedException)
        {
            // The session already started (copying, or on a GPU that failed) is of no use to a viewer who is turned away.
            await _timeshiftCoordinator.StopAsync(sessionKey);
            throw;
        }

        return run;
    }

    // An encode takes a GPU or CPU slot. The slot is held from the moment it is chosen until the session
    // that uses it is registered, so two starts at the same moment can't both take the last one.
    // launchCopy: whether the session is replaced even when transcoding turns out to be off and the
    // video is copied. When it is, the session being replaced is stopped first, so stopping it (a moment)
    // isn't done while other starts wait for the slots.
    private async Task<(TimeshiftVideoArguments Video, TimeshiftRun? Run)> LaunchEncodeAsync(
        LiveVideoEncode encode, LiveVideoFormat? source, bool onCpu, bool launchCopy, Guid channelId, Guid sessionKey, IptvUpstream upstream, string? secret, LiveSessionKind kind)
    {
        if (launchCopy) await _timeshiftCoordinator.StopAsync(sessionKey);

        using var slot = await _encodeSlots.HoldAsync();
        var video = await _timeshiftVideo.EncodeArgumentsAsync(encode, sessionKey, onCpu);
        if (!video.Encodes && !launchCopy) return (video, null);

        return (video, await LaunchTimeshiftAsync(sessionKey, channelId, encode, source, upstream, secret, video, kind));
    }

    private async Task<TimeshiftRun?> LaunchTimeshiftAsync(
        Guid profileId, Guid channelId, LiveVideoEncode? encode, LiveVideoFormat? source, IptvUpstream upstream, string? secret, TimeshiftVideoArguments video, LiveSessionKind kind)
    {
        var sessionPath = await PrepareTimeshiftDirectoryAsync(profileId);
        var outputPath = Path.Combine(sessionPath, PlaylistFileName).Replace("\\", "/");
        var segmentPath = Path.Combine(sessionPath, SegmentFilePattern).Replace("\\", "/");

        var process = BuildTimeshiftProcess(upstream.Url, upstream.Policy, outputPath, segmentPath, video, kind == LiveSessionKind.Remux);

        await _timeshiftCoordinator.StopAsync(profileId);
        var share = new LiveSessionShare(channelId, video.Encodes ? encode : null, source);
        if (!_timeshiftCoordinator.TryRegister(profileId, process, sessionPath, kind, video.Encodes ? new RunningEncode(video.OnGpu) : null, share))
        {
            process.Dispose();
            TryDeleteDirectory(sessionPath);
            return null;
        }
        var diagnostics = new RunDiagnostics();
        process.ErrorDataReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line.Data))
            {
                if (IsUpstreamBusy(line.Data)) diagnostics.UpstreamBusy = true;
                _logger.LogDebug("Timeshift ffmpeg for profile {ProfileId}: {Output}", profileId, IptvUrlRedactor.Redact(line.Data, secret));
            }
        };
        process.Start();
        process.BeginErrorReadLine();
        return new TimeshiftRun(process, sessionPath, outputPath, DateTime.UtcNow + InitializationTimeout, diagnostics);
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

    internal static Process BuildTimeshiftProcess(string streamUrl, IptvRequestPolicy policy, string outputPath, string segmentPath, TimeshiftVideoArguments video, bool rollingWindow = false)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var args = process.StartInfo.ArgumentList;
        args.Add("-hide_banner");
        args.Add("-nostats");
        args.Add("-loglevel"); args.Add("warning");
        foreach (var argument in video.BeforeInput) args.Add(argument);
        args.Add("-reconnect"); args.Add("1");
        args.Add("-reconnect_streamed"); args.Add("1");
        args.Add("-reconnect_delay_max"); args.Add("5");
        foreach (var argument in policy.FfmpegInputOptions()) args.Add(argument);
        args.Add("-i"); args.Add(policy.FfmpegInputUrl(streamUrl));
        foreach (var argument in video.Video) args.Add(argument);
        // Stereo AAC. ffmpeg's AAC encoder writes a 5.1(side) source (the usual
        // layout of a channel's AC-3 surround) with a program config element
        // and channel count 0 in every ADTS header, which Android's decoder
        // fails on (ERROR_CODE_DECODING_FAILED); 128k is a stereo bitrate anyway.
        args.Add("-c:a"); args.Add("aac");
        args.Add("-ac"); args.Add("2");
        args.Add("-b:a"); args.Add("128k");
        args.Add("-f"); args.Add("hls");
        args.Add("-hls_time"); args.Add((rollingWindow ? LiveRemuxSegmentSeconds : LiveVideoDecision.TimeshiftSegmentSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (rollingWindow)
        {
            args.Add("-hls_list_size"); args.Add(LiveRemuxWindowSegments.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("-hls_flags"); args.Add("delete_segments+temp_file");
        }
        else
        {
            args.Add("-hls_list_size"); args.Add("0");
            args.Add("-hls_flags"); args.Add("append_list+temp_file");
        }
        args.Add("-hls_segment_filename"); args.Add(segmentPath);
        args.Add(outputPath);

        return process;
    }

    private async Task SyncChannelsAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default)
    {
        if (playlist.SourceType == IptvSourceType.M3u && string.IsNullOrWhiteSpace(playlist.M3uUrl)) return;

        string? password = null;
        try
        {
            password = _upstreamResolver.PasswordFor(playlist);
            var load = await _channelLoader.LoadAsync(playlist, cancellationToken);
            await _repository.UpdateChannelsAsync(playlist.Id, load.Channels);

            if (load.Account is not null)
            {
                IptvPlaylistSettings.RecordAccount(playlist, load.Account, DateTime.UtcNow);
            }

            if (load.Device is not null)
            {
                IptvPlaylistSettings.RecordDevice(playlist, load.Device, DateTime.UtcNow);
            }

            playlist.LastError = null;
            playlist.LastSyncedAt = DateTime.UtcNow;
            await _repository.UpdatePlaylistAsync(playlist);

            _logger.LogInformation("Successfully parsed and saved {ChannelCount} channels for {PlaylistName}.", load.Channels.Count, playlist.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (XtreamAccountException ex)
        {
            IptvPlaylistSettings.RecordAccount(playlist, ex.Account, DateTime.UtcNow);
            await RecordSyncFailureAsync(playlist, ex.Message);
        }
        catch (XtreamAuthenticationException ex)
        {
            IptvPlaylistSettings.ForgetAccount(playlist);
            await RecordSyncFailureAsync(playlist, ex.Message);
        }
        catch (IptvSourceException ex)
        {
            await RecordSyncFailureAsync(playlist, IptvUrlRedactor.Redact(ex.Message, password));
        }
        catch (Exception ex)
        {
            _logger.LogError("Loading the channels of {PlaylistName} failed with {ErrorType}.", playlist.Name, ex.GetType().Name);
            await RecordSyncFailureAsync(playlist, IptvUrlRedactor.Redact(ex.Message, password));
        }
    }

    private async Task RecordSyncFailureAsync(IptvPlaylist playlist, string error)
    {
        playlist.LastError = error;
        await _repository.UpdatePlaylistAsync(playlist);
        _logger.LogError("Failed to load the channels of {PlaylistName}: {Error}", playlist.Name, error);
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
