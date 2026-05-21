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

    public TimeSpan IptvSyncTime { get; set; } = new(4, 0, 0);

    public string FolderWatcherProviderId { get; set; } = "polling_watcher";
    public int FolderWatcherPollingInterval { get; set; } = 30;
    public string LocalMediaScannerProviderId { get; set; } = "Vora_scanner";

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

    public string AdminThemeId { get; set; } = "vora-default";

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
