using System.Linq.Expressions;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Settings.ViewModels;

public class ServerSettingsVM
{
    public string ServerName { get; set; } = "Vora Server";
    public bool EnableNightlyScan { get; set; }
    public string NightlyScanTime { get; set; } = "02:00";
    public int RegistrationMode { get; set; }
    public int RunDetections { get; set; }
    public string DetectionScheduleTime { get; set; } = "03:00";
    public string FolderWatcherProviderId { get; set; } = "polling_watcher";
    public int FolderWatcherPollingInterval { get; set; } = 30;
    public string LocalMediaScannerProviderId { get; set; } = "Vora_scanner";
    public int InternetUploadSpeedMbps { get; set; }
    public int MaxRemoteStreamBitrateMbps { get; set; }
    public int TranscodeQuality { get; set; }
    public string TranscoderTempDirectory { get; set; } = "/transcode";
    public int BackgroundX264Preset { get; set; }
    public bool EnableHdrToneMapping { get; set; }
    public bool DisableVideoTranscoding { get; set; }
    public bool UseHardwareAcceleration { get; set; }
    public bool UseHardwareEncoding { get; set; }
    public int EnableHevcEncoding { get; set; }
    public bool EnableHevcOptimization { get; set; }
    public int MaxGpuTranscodes { get; set; }
    public int MaxCpuTranscodes { get; set; }
    public int MaxBackgroundTranscodes { get; set; }
    public string HardwareTranscodingDevice { get; set; } = "Auto";
    public int TranscoderThrottleBuffer { get; set; }
    public string TonemappingAlgorithm { get; set; } = string.Empty;
    public int StreamingProfile { get; set; } = 0;

    public bool EnableDailyMixes { get; set; } = true;
    public string DailyMixSchedule { get; set; } = "Daily3am";
    public int DailyMixCount { get; set; } = 6;
    public int DailyMixSize { get; set; } = 50;
    public int DailyMixDriftPercent { get; set; } = 20;
    public int DailyMixMinPlays { get; set; } = 50;
    public DateTime? DailyMixLastRefreshedAt { get; set; }
    public bool EnableWeeklyMixes { get; set; } = true;
    public DateTime? WeeklyMixLastRefreshedAt { get; set; }

    public string? DvrStoragePath { get; set; }
    public long DvrMaxStorageGb { get; set; }
    public int DvrStorageWarningPercent { get; set; } = 90;
    public int DvrAutoDeleteWatchedDays { get; set; }
    public int DvrDefaultSeriesRetention { get; set; }
    public bool DvrNotifyOnFailure { get; set; } = true;
    public bool DvrNotifyOnStorageThreshold { get; set; } = true;
    public int DvrPreRollSeconds { get; set; } = 120;
    public int DvrPostRollSeconds { get; set; } = 300;
    public string DvrConflictPolicy { get; set; } = "AlwaysRecord";

    public static Expression<Func<ServerSetting, ServerSettingsVM>> Projection =>
        s => new ServerSettingsVM
        {
            ServerName = s.ServerName,
            EnableNightlyScan = s.EnableNightlyScan,
            NightlyScanTime = s.NightlyScanTime.ToString(@"hh\:mm"),
            RegistrationMode = (int)s.RegistrationMode,
            RunDetections = (int)s.RunDetections,
            DetectionScheduleTime = s.DetectionScheduleTime.ToString(@"hh\:mm"),
            FolderWatcherProviderId = s.FolderWatcherProviderId,
            FolderWatcherPollingInterval = s.FolderWatcherPollingInterval,
            LocalMediaScannerProviderId = s.LocalMediaScannerProviderId,
            InternetUploadSpeedMbps = s.InternetUploadSpeedMbps,
            MaxRemoteStreamBitrateMbps = s.MaxRemoteStreamBitrateMbps,
            TranscodeQuality = s.TranscodeQuality,
            TranscoderTempDirectory = s.TranscoderTempDirectory,
            BackgroundX264Preset = s.BackgroundX264Preset,
            EnableHdrToneMapping = s.EnableHdrToneMapping,
            DisableVideoTranscoding = s.DisableVideoTranscoding,
            UseHardwareAcceleration = s.UseHardwareAcceleration,
            UseHardwareEncoding = s.UseHardwareEncoding,
            EnableHevcEncoding = s.EnableHevcEncoding,
            EnableHevcOptimization = s.EnableHevcOptimization,
            MaxGpuTranscodes = s.MaxGpuTranscodes,
            MaxCpuTranscodes = s.MaxCpuTranscodes,
            MaxBackgroundTranscodes = s.MaxBackgroundTranscodes,
            HardwareTranscodingDevice = s.HardwareTranscodingDevice,
            TranscoderThrottleBuffer = s.TranscoderThrottleBuffer,
            TonemappingAlgorithm = s.TonemappingAlgorithm,
            StreamingProfile = (int)s.StreamingProfile,
            EnableDailyMixes = s.EnableDailyMixes,
            DailyMixSchedule = s.DailyMixSchedule,
            DailyMixCount = s.DailyMixCount,
            DailyMixSize = s.DailyMixSize,
            DailyMixDriftPercent = s.DailyMixDriftPercent,
            DailyMixMinPlays = s.DailyMixMinPlays,
            DailyMixLastRefreshedAt = s.DailyMixLastRefreshedAt,
            EnableWeeklyMixes = s.EnableWeeklyMixes,
            WeeklyMixLastRefreshedAt = s.WeeklyMixLastRefreshedAt,
            DvrStoragePath = s.DvrStoragePath,
            DvrMaxStorageGb = s.DvrMaxStorageGb,
            DvrStorageWarningPercent = s.DvrStorageWarningPercent,
            DvrAutoDeleteWatchedDays = s.DvrAutoDeleteWatchedDays,
            DvrDefaultSeriesRetention = s.DvrDefaultSeriesRetention,
            DvrNotifyOnFailure = s.DvrNotifyOnFailure,
            DvrNotifyOnStorageThreshold = s.DvrNotifyOnStorageThreshold,
            DvrPreRollSeconds = s.DvrPreRollSeconds,
            DvrPostRollSeconds = s.DvrPostRollSeconds,
            DvrConflictPolicy = s.DvrConflictPolicy.ToString()
        };
}
