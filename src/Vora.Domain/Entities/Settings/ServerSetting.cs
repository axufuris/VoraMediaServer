using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Settings;

public class ServerSetting
{
    public string Id { get; set; } = "GLOBAL_SETTINGS";
    public string ServerName { get; set; } = "Vora Server";

    public RegistrationMode RegistrationMode { get; set; } = RegistrationMode.SecretWord;

    public string? TmdbApiKey { get; set; } = "37627bd54505a2f5f83df81303bc1eaa";
    public string? TvdbApiKey { get; set; } = "56c51421-6057-4825-bc6f-a550198c8fcc";
    public string? TvdbToken { get; set; }

    public bool EnableNightlyScan { get; set; } = true;
    public TimeSpan NightlyScanTime { get; set; } = new(2, 0, 0);

    public DetectionTrigger RunDetections { get; set; } = DetectionTrigger.OnSchedule;
    public TimeSpan DetectionScheduleTime { get; set; } = new(3, 0, 0);

    public int SilenceThresholdOffsetDb { get; set; } = -12;
    public double SilenceMinDurationMovieSec { get; set; } = 1.5;
    public double SilenceMinDurationEpisodeSec { get; set; } = 1.0;
    public double BlackFrameMinDurationSec { get; set; } = 0.5;
    public int EpisodeIntroClusterToleranceSec { get; set; } = 5;
    public int EpisodeIntroClusterMinAgreementPct { get; set; } = 70;

    public TimeSpan VideoThumbnailScheduleTime { get; set; } = new(4, 0, 0);
    public int VideoThumbnailIntervalSeconds { get; set; } = 10;
    public int VideoThumbnailWidth { get; set; } = 320;
    public int VideoThumbnailHeight { get; set; } = 180;
    public int VideoThumbnailJpegQuality { get; set; } = 5;
    public int VideoThumbnailSpriteColumns { get; set; } = 10;

    public TimeSpan IptvSyncTime { get; set; } = new(4, 0, 0);

    public string FolderWatcherProviderId { get; set; } = "polling_watcher";
    public int FolderWatcherPollingInterval { get; set; } = 30;
    public string LocalMediaScannerProviderId { get; set; } = "Vora_scanner";

    public bool EnableTrashAutoPurge { get; set; } = true;
    public int MissingMediaRetentionDays { get; set; } = 30;

    public bool ResolveMovieTvdbIds { get; set; }

    public bool EnableRemoteAccess { get; set; } = true;
    public bool ManuallySpecifyPublicPort { get; set; }
    public int PublicPort { get; set; } = 32080;

    public int InternetUploadSpeedMbps { get; set; } = 1000;
    public int MaxRemoteStreamBitrateMbps { get; set; }

    public StreamingProfile StreamingProfile { get; set; } = StreamingProfile.ClientPreference;

    public bool DisableVideoTranscoding { get; set; }
    public bool UseHardwareAcceleration { get; set; } = true;
    public bool UseHardwareEncoding { get; set; } = true;
    public string HardwareTranscodingDevice { get; set; } = "Auto";

    public int TranscodeQuality { get; set; }
    public int BackgroundX264Preset { get; set; } = 2;
    public int EnableHevcEncoding { get; set; } = 1;
    public bool EnableHevcOptimization { get; set; } = true;
    public bool EnableHdrToneMapping { get; set; } = true;
    public string TonemappingAlgorithm { get; set; } = "hable";

    // HDR tonemap quality / downscale resolution mode.
    //
    // Both default to "Auto", which means the FFmpeg layer picks based
    // on detected host environment at startup:
    //   - WSL2/Docker Desktop on Windows: Vulkan + OpenCL aren't
    //     exposed through to the container, so GPU HDR tonemap is
    //     impossible. Auto → Fast tonemap + downscale 4K HDR sources
    //     to 1080p on the GPU before tonemap so the CPU work stays
    //     real-time.
    //   - Native Linux / Unraid: GPU HDR tonemap is available (and
    //     will become first-class once we move to jellyfin-ffmpeg per
    //     task #242). Auto → Quality tonemap, no automatic downscale.
    //
    // Users can lock either to a specific value via the admin UI:
    //   HdrTonemapQuality: Auto | Quality | Fast | Off
    //     Quality = full zscale + hable chain (most accurate, slow on CPU)
    //     Fast    = single-pass colorspace conversion (washes HDR
    //               highlights but real-time on CPU at 4K)
    //     Off     = bit-depth reduction only, no colorspace conversion
    //               (HDR values interpreted as SDR, looks wrong but
    //               very fast)
    //   HdrTranscodeDownscale: Auto | Always | Never
    //     Always = downscale 4K HDR sources to 1080p before tonemap
    //               regardless of the user's quality pick
    //     Never  = preserve source resolution end-to-end
    public string HdrTonemapQuality { get; set; } = "Auto";
    public string HdrTranscodeDownscale { get; set; } = "Auto";

    public string TranscoderTempDirectory { get; set; } = "/transcode";
    public int TranscoderThrottleBuffer { get; set; } = 60;

    public int MaxGpuTranscodes { get; set; } = 2;
    public int MaxCpuTranscodes { get; set; }
    public int MaxBackgroundTranscodes { get; set; }

    public bool EnableDailyMixes { get; set; } = true;
    public string DailyMixSchedule { get; set; } = "Daily3am";
    public int DailyMixCount { get; set; } = 6;
    public int DailyMixSize { get; set; } = 50;
    public int DailyMixDriftPercent { get; set; } = 20;
    public int DailyMixMinPlays { get; set; } = 50;
    public DateTime? DailyMixLastRefreshedAt { get; set; }

    public bool EnableWeeklyMixes { get; set; } = true;
    public DateTime? WeeklyMixLastRefreshedAt { get; set; }

    public bool EnableDiscover { get; set; } = true;
    public bool EnableForYou { get; set; } = true;
    public bool EnableReleaseCalendar { get; set; } = true;
    public bool EnableLiveTv { get; set; } = true;
    public bool EnableDvr { get; set; } = true;
    public bool EnableInternetRadio { get; set; } = true;
    public bool EnablePodcasts { get; set; } = true;

    public string? DvrStoragePath { get; set; }
    public long DvrMaxStorageGb { get; set; }
    public int DvrStorageWarningPercent { get; set; } = 90;
    public int DvrAutoDeleteWatchedDays { get; set; }
    public int DvrDefaultSeriesRetention { get; set; }
    public bool DvrNotifyOnFailure { get; set; } = true;
    public bool DvrNotifyOnStorageThreshold { get; set; } = true;

    public int DvrPreRollSeconds { get; set; } = 120;
    public int DvrPostRollSeconds { get; set; } = 300;
    public DvrConflictPolicy DvrConflictPolicy { get; set; } = DvrConflictPolicy.AlwaysRecord;

    public int TimeshiftMaxSessionHours { get; set; } = 6;

    public string AdminThemeId { get; set; } = "vora-dark";

    public string DefaultClientTemplateId { get; set; } = "vora-cinema";

    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseStartTls { get; set; } = true;
    public bool SmtpUseImplicitSsl { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPasswordCiphertext { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromDisplayName { get; set; }
    public string? EmailPublicBaseUrl { get; set; }

    public string? BackupConfigurationJson { get; set; }
}
