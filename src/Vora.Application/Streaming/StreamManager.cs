using System.Diagnostics;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Streaming;

public record DeviceCapsDto(string[] VideoCodecs, string[] AudioCodecs, string[] Containers, int MaxAudioChannels, int ClientBandwidthKbps, int RequestedClientBitrateKbps, int RequestedMaxResolution = 0);

public interface IStreamManager
{
    Task<(StreamSession Session, string StreamUrl)> StartSessionAsync(Guid mediaId, string deviceId, Guid userId, Guid? profileId, double startPosition, Guid? videoTrackId = null, Guid? audioTrackId = null, Guid? subtitleTrackId = null, DeviceCapsDto? capabilities = null, Guid? mediaPartId = null);
    Task<(StreamSession Session, string StreamUrl)> StartExtraSessionAsync(Guid extraId, string deviceId, Guid userId, Guid? profileId, double startPosition, DeviceCapsDto? capabilities = null);
    Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(int page, int pageSize, string search);
    Task PingSessionAsync(Guid sessionId, double currentPosition, double duration, bool isPaused);
    Task<List<NowPlayingSessionDto>> GetNowPlayingSessionsAsync();
    Task StopSessionAsync(Guid sessionId);
    Task<string?> GetPlayableFilePathAsync(Guid sessionId);
    Task<SystemStatsVM> GetSystemStatsAsync(ISystemMetricRepository metricRepo);
}

public class StreamManager : IStreamManager
{
    public const string PlayTokenScope = "play";
    public static readonly TimeSpan PlayTokenTtl = TimeSpan.FromHours(4);

    private readonly IStreamRepository _repository;
    private readonly IBestPathDecisionManager _decisionManager;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IStreamingTokenSigner _tokenSigner;
    private readonly IClientNotifier _notifier;
    private readonly ITranscodeService _transcodeService;
    private readonly StoragePathsOptions _storagePaths;

    public StreamManager(IStreamRepository repository, IBestPathDecisionManager decisionManager, ISystemSettingsRepository settingsRepo, IStreamingTokenSigner tokenSigner, IClientNotifier notifier, ITranscodeService transcodeService, IOptions<StoragePathsOptions> storagePaths)
    {
        _repository = repository;
        _decisionManager = decisionManager;
        _settingsRepo = settingsRepo;
        _tokenSigner = tokenSigner;
        _notifier = notifier;
        _transcodeService = transcodeService;
        _storagePaths = storagePaths.Value;
    }

    public async Task<(StreamSession Session, string StreamUrl)> StartSessionAsync(Guid mediaId, string deviceId, Guid userId, Guid? profileId, double startPosition, Guid? videoTrackId = null, Guid? audioTrackId = null, Guid? subtitleTrackId = null, DeviceCapsDto? capabilities = null, Guid? mediaPartId = null)
    {
        var mediaInfo = await _repository.GetMediaStreamInfoAsync(mediaId);
        if (mediaInfo == null || !mediaInfo.Parts.Any()) throw new InvalidOperationException("Media not found or has no parts.");

        var client = await _repository.GetClientDeviceAsync(deviceId);
        if (client == null) throw new InvalidOperationException("Unknown Device.");

        var settings = await _settingsRepo.GetSettingsAsync();

        if (capabilities != null)
        {
            client.SupportedVideoCodecs = capabilities.VideoCodecs?.ToList() ?? new List<string>();
            client.SupportedAudioCodecs = capabilities.AudioCodecs?.ToList() ?? new List<string>();
            client.SupportedContainers = capabilities.Containers?.ToList() ?? new List<string>();
            client.MaxAudioChannels = capabilities.MaxAudioChannels;
        }

        await _repository.EndActiveSessionsForDeviceAsync(client.Id);

        bool isRemote = !IsLocalIp(client.LastIpAddress);
        int maxAllowedBandwidthKbps = 0;
        string bandwidthLimitSource = "None";

        if (isRemote)
        {
            int uploadLimitKbps = settings.InternetUploadSpeedMbps * 1000;
            int remoteLimitKbps = settings.MaxRemoteStreamBitrateMbps * 1000;

            int currentRemoteBandwidthKbps = 0;
            if (uploadLimitKbps > 0)
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
                var activeSessions = await _repository.GetNowPlayingSessionsAsync(cutoffTime);

                currentRemoteBandwidthKbps = activeSessions
                    .Where(s => !IsLocalIp(s.IpAddress) && s.DeviceId != deviceId)
                    .Sum(s => s.BandwidthKbps);
            }

            int remainingUploadKbps = uploadLimitKbps > 0 ? Math.Max(0, uploadLimitKbps - currentRemoteBandwidthKbps) : 0;

            if (remoteLimitKbps > 0)
            {
                maxAllowedBandwidthKbps = remoteLimitKbps;
                bandwidthLimitSource = "Server Limit";
            }
            else if (uploadLimitKbps > 0)
            {
                maxAllowedBandwidthKbps = remainingUploadKbps;
                bandwidthLimitSource = "Server Upload Limit";
            }

            if (uploadLimitKbps > 0 && maxAllowedBandwidthKbps > remainingUploadKbps)
            {
                maxAllowedBandwidthKbps = remainingUploadKbps;
                bandwidthLimitSource = "Server Upload Limit";
            }
        }

        if (capabilities != null && capabilities.RequestedClientBitrateKbps > 0)
        {
            if (maxAllowedBandwidthKbps == 0 || capabilities.RequestedClientBitrateKbps < maxAllowedBandwidthKbps)
            {
                maxAllowedBandwidthKbps = capabilities.RequestedClientBitrateKbps;
                bandwidthLimitSource = "Client Limit";
            }
        }

        var decision = await _decisionManager.DetermineBestPathAsync(client, mediaInfo, maxAllowedBandwidthKbps, bandwidthLimitSource, videoTrackId, audioTrackId, subtitleTrackId, capabilities?.RequestedMaxResolution ?? 0, mediaPartId);

        var selectedPart = mediaInfo.Parts.FirstOrDefault(p => p.Id == decision.SelectedMediaPartId);
        var selectedVideo = selectedPart?.VideoTracks.FirstOrDefault(v => v.Id == decision.SelectedVideoTrackId);

        var session = new StreamSession
        {
            ClientDeviceId = client.Id,
            MediaItemId = mediaId,
            UserId = userId,
            UserProfileId = profileId,
            Strategy = decision.Strategy.ToString(),
            VideoStrategy = decision.VideoStrategy,
            AudioStrategy = decision.AudioStrategy,
            SubtitleStrategy = decision.SubtitleStrategy,
            VideoCodec = decision.TargetVideoCodec,
            AudioCodec = decision.TargetAudioCodec,
            Container = decision.TargetContainer,
            Resolution = selectedPart?.Resolution,
            HdrType = selectedVideo?.HdrType,
            OutputResolution = decision.OutputResolution,
            OutputHdrType = decision.OutputHdrType,
            StartPosition = startPosition,
            CurrentPosition = startPosition,
            MediaPartId = decision.SelectedMediaPartId,
            VideoTrackId = decision.SelectedVideoTrackId,
            AudioTrackId = decision.SelectedAudioTrackId,
            SubtitleTrackId = decision.SelectedSubtitleTrackId,
            TargetAudioChannels = decision.TargetAudioChannels,
            IsSubtitleBurnIn = decision.RequiresSubtitleBurnIn,
            Quality = decision.Quality,
            BandwidthKbps = decision.BandwidthKbps,
            DecisionLog = decision.GetDecisionLogJson()
        };

        var createdSession = await _repository.CreateSessionAsync(session);

        var playToken = _tokenSigner.Sign(PlayTokenScope, createdSession.Id.ToString(), PlayTokenTtl);
        return (createdSession, $"/api/streaming/play/{createdSession.Id}?t={playToken}");
    }

    public async Task<(StreamSession Session, string StreamUrl)> StartExtraSessionAsync(Guid extraId, string deviceId, Guid userId, Guid? profileId, double startPosition, DeviceCapsDto? capabilities = null)
    {
        var extra = await _repository.GetMediaExtraAsync(extraId);
        if (extra == null) throw new InvalidOperationException("Extra not found.");

        var streamInfo = await _repository.GetExtraStreamInfoAsync(extraId);
        if (streamInfo == null || !streamInfo.Parts.Any()) throw new InvalidOperationException("Extra has not been analyzed yet.");

        var client = await _repository.GetClientDeviceAsync(deviceId);
        if (client == null) throw new InvalidOperationException("Unknown Device.");

        if (capabilities != null)
        {
            client.SupportedVideoCodecs = capabilities.VideoCodecs?.ToList() ?? new List<string>();
            client.SupportedAudioCodecs = capabilities.AudioCodecs?.ToList() ?? new List<string>();
            client.SupportedContainers = capabilities.Containers?.ToList() ?? new List<string>();
            client.MaxAudioChannels = capabilities.MaxAudioChannels;
        }

        await _repository.EndActiveSessionsForDeviceAsync(client.Id);

        var decision = await _decisionManager.DetermineBestPathAsync(client, streamInfo, 0, "None", null, null, null, capabilities?.RequestedMaxResolution ?? 0);

        var selectedPart = streamInfo.Parts.FirstOrDefault(p => p.Id == decision.SelectedMediaPartId);
        var selectedVideo = selectedPart?.VideoTracks.FirstOrDefault(v => v.Id == decision.SelectedVideoTrackId);

        var session = new StreamSession
        {
            ClientDeviceId = client.Id,
            MediaItemId = extra.MediaItemId,
            ExtraId = extra.Id,
            UserId = userId,
            UserProfileId = profileId,
            Strategy = decision.Strategy.ToString(),
            VideoStrategy = decision.VideoStrategy,
            AudioStrategy = decision.AudioStrategy,
            SubtitleStrategy = decision.SubtitleStrategy,
            VideoCodec = decision.TargetVideoCodec,
            AudioCodec = decision.TargetAudioCodec,
            Container = decision.TargetContainer,
            Resolution = selectedPart?.Resolution,
            HdrType = selectedVideo?.HdrType,
            OutputResolution = decision.OutputResolution,
            OutputHdrType = decision.OutputHdrType,
            StartPosition = startPosition,
            CurrentPosition = startPosition,
            MediaPartId = decision.SelectedMediaPartId,
            VideoTrackId = decision.SelectedVideoTrackId,
            AudioTrackId = decision.SelectedAudioTrackId,
            SubtitleTrackId = decision.SelectedSubtitleTrackId,
            TargetAudioChannels = decision.TargetAudioChannels,
            IsSubtitleBurnIn = decision.RequiresSubtitleBurnIn,
            Quality = decision.Quality,
            BandwidthKbps = decision.BandwidthKbps,
            DecisionLog = decision.GetDecisionLogJson()
        };

        var createdSession = await _repository.CreateSessionAsync(session);
        var playToken = _tokenSigner.Sign(PlayTokenScope, createdSession.Id.ToString(), PlayTokenTtl);
        return (createdSession, $"/api/streaming/play/{createdSession.Id}?t={playToken}");
    }

    public async Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(int page, int pageSize, string search)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return await _repository.GetGroupedHistoryAsync(page, pageSize, search);
    }

    public async Task PingSessionAsync(Guid sessionId, double currentPosition, double duration, bool isPaused)
    {
        var session = await _repository.GetSessionAsync(sessionId);
        if (session == null || session.EndedAt.HasValue) return;

        var now = DateTime.UtcNow;
        if (isPaused) session.TotalPausedDuration += (now - session.LastPingAt).TotalSeconds;

        session.CurrentPosition = currentPosition;
        session.IsPaused = isPaused;
        session.LastPingAt = now;

        _transcodeService.TouchSession(session.ExtraId ?? session.MediaItemId);

        await _repository.UpdateSessionAsync(session);

        // Extras (trailers/featurettes) must not write progress onto the parent
        // media item — otherwise watching a 2-minute trailer marks the movie
        // as watched.
        if (!session.ExtraId.HasValue && session.UserProfileId.HasValue)
        {
            await _repository.UpdateUserMediaStateAsync(session.UserProfileId.Value, session.MediaItemId, currentPosition, duration);
        }
    }

    public async Task<List<NowPlayingSessionDto>> GetNowPlayingSessionsAsync()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-1);
        return await _repository.GetNowPlayingSessionsAsync(cutoffTime);
    }

    public async Task StopSessionAsync(Guid sessionId)
    {
        var session = await _repository.GetSessionAsync(sessionId);
        if (session != null)
        {
            session.EndedAt = DateTime.UtcNow;
            await _repository.UpdateSessionAsync(session);

            if (session.UserProfileId.HasValue)
            {
                await _notifier.NotifyUserMediaStateUpdatedAsync(session.UserProfileId.Value);
            }
        }
    }

    public async Task<string?> GetPlayableFilePathAsync(Guid sessionId)
    {
        var part = await _repository.GetMediaPartForSessionAsync(sessionId);
        if (part == null || !File.Exists(part.FilePath)) return null;

        return part.FilePath;
    }

    private bool IsLocalIp(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return false;
        if (ipAddress == "127.0.0.1" || ipAddress == "::1") return true;
        if (ipAddress.StartsWith("192.168.")) return true;
        if (ipAddress.StartsWith("10.")) return true;
        if (ipAddress.StartsWith("172."))
        {
            var parts = ipAddress.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out int secondOctet))
            {
                if (secondOctet >= 16 && secondOctet <= 31) return true;
            }
        }
        return false;
    }

    public async Task<SystemStatsVM> GetSystemStatsAsync(ISystemMetricRepository metricRepo)
    {
        var latestMetric = await metricRepo.GetLatestMetricAsync();

        var cpu = latestMetric?.CpuUsagePercentage ?? 0.0;
        var ramBytes = Process.GetCurrentProcess().WorkingSet64;
        var ramGb = ramBytes / (1024.0 * 1024.0 * 1024.0);

        // Disk stats for the volume that actually holds Vora's data. In a
        // container the data path (e.g. /app/data) is a bind mount, so it lives
        // on a different filesystem than the container root "/". On Linux
        // Path.GetPathRoot always returns "/", which would report the Docker
        // vDisk instead — so resolve the mount whose mount point is the longest
        // prefix of the data path (a bind mount is its own DriveInfo entry).
        long diskTotal = 0;
        long diskFree = 0;
        try
        {
            var dataDrive = ResolveDataDrive();
            if (dataDrive?.IsReady == true)
            {
                diskTotal = dataDrive.TotalSize;
                diskFree = dataDrive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Some platforms / mounts don't expose drive info; report zeros and
            // let the frontend show "—" rather than crash the whole stats endpoint.
        }

        // "Used" is Vora's OWN footprint (its data directories), not the whole
        // volume's used space. The data volume usually shares a disk with the
        // media libraries, so whole-volume used would count the library content
        // the admin never asked about here.
        return new SystemStatsVM
        {
            CpuUsagePercentage = Math.Round(cpu, 1),
            RamUsageGb = Math.Round(ramGb, 2),
            DiskTotalBytes = diskTotal,
            DiskUsedBytes = GetAppStorageUsedBytes(),
            DiskFreeBytes = diskFree,
        };
    }

    private DriveInfo? ResolveDataDrive()
    {
        var candidates = new[]
        {
            _storagePaths.Metadata,
            _storagePaths.CustomArtwork,
            _storagePaths.Backups,
            _storagePaths.VideoThumbnails,
            _storagePaths.UserImages,
        };

        var target = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            ?? AppContext.BaseDirectory;
        target = Path.GetFullPath(target);

        DriveInfo? best = null;
        var bestLen = -1;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var mount = drive.RootDirectory.FullName;
                if (!PathIsUnder(target, mount)) continue;
                if (mount.Length > bestLen)
                {
                    best = drive;
                    bestLen = mount.Length;
                }
            }
            catch
            {
            }
        }

        return best;
    }

    private static bool PathIsUnder(string path, string mount)
    {
        if (mount == "/" || mount.Length == 0) return true;
        var normalized = mount.TrimEnd('/', '\\');
        if (normalized.Length == 0) return true;
        return string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    // Sizing the data directories walks every file, so cache it — the dashboard
    // polls these stats often and the footprint barely moves minute to minute.
    private static readonly TimeSpan AppStorageCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly object AppStorageLock = new();
    private static long _cachedAppStorageBytes = -1;
    private static DateTime _cachedAppStorageAtUtc = DateTime.MinValue;

    private long GetAppStorageUsedBytes()
    {
        lock (AppStorageLock)
        {
            if (_cachedAppStorageBytes >= 0 && DateTime.UtcNow - _cachedAppStorageAtUtc < AppStorageCacheTtl)
            {
                return _cachedAppStorageBytes;
            }
        }

        var total = ComputeAppStorageUsedBytes();

        lock (AppStorageLock)
        {
            _cachedAppStorageBytes = total;
            _cachedAppStorageAtUtc = DateTime.UtcNow;
        }
        return total;
    }

    private long ComputeAppStorageUsedBytes()
    {
        var roots = new[]
        {
            _storagePaths.Metadata,
            _storagePaths.CustomArtwork,
            _storagePaths.OriginalArtworkCache,
            _storagePaths.UserImages,
            _storagePaths.Plugins,
            _storagePaths.VideoThumbnails,
            _storagePaths.Logs,
            _storagePaths.Backups,
            _storagePaths.DataProtection,
            _storagePaths.EpgCache,
            _storagePaths.IptvDvr,
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.GetFullPath(p!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        // Drop any path nested under another so a shared base isn't double
        // counted (Logs/Backups under the data dir, imagecache under CustomArtwork).
        var topLevel = roots
            .Where(p => !roots.Any(other => !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) && PathIsUnder(p, other)))
            .ToList();

        long total = 0;
        foreach (var root in topLevel)
        {
            total += DirectorySizeBytes(root);
        }
        return total;
    }

    private static long DirectorySizeBytes(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long sum = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { sum += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch
        {
        }
        return sum;
    }
}
