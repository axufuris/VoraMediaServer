using System.Diagnostics;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Streaming;

public record DeviceCapsDto(string[] VideoCodecs, string[] AudioCodecs, string[] Containers, int MaxAudioChannels, int ClientBandwidthKbps, int RequestedClientBitrateKbps, int RequestedMaxResolution = 0);

public interface IStreamManager
{
    Task<(StreamSession Session, string StreamUrl)> StartSessionAsync(Guid mediaId, string deviceId, Guid userId, Guid? profileId, double startPosition, Guid? videoTrackId = null, Guid? audioTrackId = null, Guid? subtitleTrackId = null, DeviceCapsDto? capabilities = null);
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

    public StreamManager(IStreamRepository repository, IBestPathDecisionManager decisionManager, ISystemSettingsRepository settingsRepo, IStreamingTokenSigner tokenSigner, IClientNotifier notifier)
    {
        _repository = repository;
        _decisionManager = decisionManager;
        _settingsRepo = settingsRepo;
        _tokenSigner = tokenSigner;
        _notifier = notifier;
    }

    public async Task<(StreamSession Session, string StreamUrl)> StartSessionAsync(Guid mediaId, string deviceId, Guid userId, Guid? profileId, double startPosition, Guid? videoTrackId = null, Guid? audioTrackId = null, Guid? subtitleTrackId = null, DeviceCapsDto? capabilities = null)
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

        var decision = await _decisionManager.DetermineBestPathAsync(client, mediaInfo, maxAllowedBandwidthKbps, bandwidthLimitSource, videoTrackId, audioTrackId, subtitleTrackId, capabilities?.RequestedMaxResolution ?? 0);

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

    public async Task<(List<HistorySessionDto> Data, int Total)> GetGroupedHistoryAsync(int page, int pageSize, string search)
    {
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

        await _repository.UpdateSessionAsync(session);

        if (session.UserProfileId.HasValue)
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

        // Disk stats for the drive hosting the API's working directory.
        // In Docker that's typically /app, which is mounted onto the host's
        // disk. DriveInfo reflects the mounted volume, which is what admins
        // care about (the disk that fills up if libraries or DVR grow).
        long diskTotal = 0;
        long diskFree = 0;
        try
        {
            var workingDrive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
            if (workingDrive.IsReady)
            {
                diskTotal = workingDrive.TotalSize;
                diskFree = workingDrive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Some platforms / mounts don't expose drive info; report zeros and
            // let the frontend show "—" rather than crash the whole stats endpoint.
        }

        return new SystemStatsVM
        {
            CpuUsagePercentage = Math.Round(cpu, 1),
            RamUsageGb = Math.Round(ramGb, 2),
            DiskTotalBytes = diskTotal,
            DiskUsedBytes = diskTotal - diskFree,
            DiskFreeBytes = diskFree,
        };
    }
}
