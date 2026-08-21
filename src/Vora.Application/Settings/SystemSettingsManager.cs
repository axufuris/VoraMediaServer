using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Plugins.ViewModels;
using Vora.Application.Settings.ViewModels;
using Vora.Application.Watchers;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Settings;

public interface ISystemSettingsManager
{
    Task<ServerSettingsVM> GetServerSettingsAsync();
    Task UpdateServerSettingsAsync(ServerSettingsVM request);
    Task<List<PluginSettingFieldVM>> GetPluginSettingsAsync(string pluginId);
    Task UpdatePluginSettingsAsync(string pluginId, Dictionary<string, string> settings);
    Task<FeatureFlagsVM> GetFeatureFlagsAsync();
    Task UpdateFeatureFlagsAsync(UpdateFeatureFlagsRequest request);
}

public class SystemSettingsManager : ISystemSettingsManager
{
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IEnumerable<IVoraPlugin> _plugins;
    private readonly IServiceProvider _serviceProvider;

    public SystemSettingsManager(ISystemSettingsRepository settingsRepo, IEnumerable<IVoraPlugin> plugins, IServiceProvider serviceProvider)
    {
        _settingsRepo = settingsRepo;
        _plugins = plugins;
        _serviceProvider = serviceProvider;
    }

    public async Task<ServerSettingsVM> GetServerSettingsAsync()
    {
        return await _settingsRepo.GetServerSettingsVMAsync();
    }

    public async Task<List<PluginSettingFieldVM>> GetPluginSettingsAsync(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return new List<PluginSettingFieldVM>();

        var definitions = plugin.GetSettingDefinitions();
        var result = new List<PluginSettingFieldVM>();

        var savedSettings = await _settingsRepo.GetAllPluginSettingsAsync(pluginId);

        savedSettings.TryGetValue("is_enabled", out var isEnabledSavedValue);
        result.Add(new PluginSettingFieldVM
        {
            Key = "is_enabled",
            Label = "Enable Plugin",
            Type = "boolean",
            Description = "Toggle this plugin on or off. If disabled, Vora will ignore it.",
            Value = isEnabledSavedValue ?? "true"
        });

        foreach (var def in definitions)
        {
            savedSettings.TryGetValue(def.Key, out var savedValue);

            result.Add(new PluginSettingFieldVM
            {
                Key = def.Key,
                Label = def.Label,
                Type = def.Type,
                Description = def.Description,
                Value = savedValue ?? def.DefaultValue,
                Placeholder = def.Placeholder,
                Required = def.Required,
                Options = def.Options
            });
        }

        return result;
    }

    public async Task UpdateServerSettingsAsync(ServerSettingsVM request)
    {
        var settings = await _settingsRepo.GetSettingsForUpdateAsync();

        bool watcherChanged = settings.FolderWatcherProviderId != request.FolderWatcherProviderId ||
                              settings.FolderWatcherPollingInterval != request.FolderWatcherPollingInterval;

        settings.ServerName = request.ServerName;

        settings.EnableNightlyScan = request.EnableNightlyScan;
        if (TimeSpan.TryParse(request.NightlyScanTime, out var nsTime)) settings.NightlyScanTime = nsTime;
        settings.ScanIgnoredFolders = (request.ScanIgnoredFolders ?? new List<string>())
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        settings.RunDetections = (DetectionTrigger)request.RunDetections;
        if (TimeSpan.TryParse(request.DetectionScheduleTime, out var dsTime)) settings.DetectionScheduleTime = dsTime;
        settings.SilenceThresholdOffsetDb = Math.Clamp(request.SilenceThresholdOffsetDb, -40, 0);
        settings.SilenceMinDurationMovieSec = Math.Clamp(request.SilenceMinDurationMovieSec, 0.2, 10.0);
        settings.SilenceMinDurationEpisodeSec = Math.Clamp(request.SilenceMinDurationEpisodeSec, 0.2, 10.0);
        settings.BlackFrameMinDurationSec = Math.Clamp(request.BlackFrameMinDurationSec, 0.1, 10.0);
        settings.EpisodeIntroClusterToleranceSec = Math.Clamp(request.EpisodeIntroClusterToleranceSec, 1, 60);
        settings.EpisodeIntroClusterMinAgreementPct = Math.Clamp(request.EpisodeIntroClusterMinAgreementPct, 50, 100);
        settings.AnalyzeConcurrency = Math.Clamp(request.AnalyzeConcurrency, 1, 16);
        settings.AnalyzeUseHardwareDecode = request.AnalyzeUseHardwareDecode;

        if (TimeSpan.TryParse(request.VideoThumbnailScheduleTime, out var vtTime)) settings.VideoThumbnailScheduleTime = vtTime;
        if (TimeSpan.TryParse(request.IptvHealthCheckTime, out var ihcTime)) settings.IptvHealthCheckTime = ihcTime;
        settings.VideoThumbnailIntervalSeconds = Math.Clamp(request.VideoThumbnailIntervalSeconds, 2, 300);
        settings.VideoThumbnailWidth = Math.Clamp(request.VideoThumbnailWidth, 80, 1280);
        settings.VideoThumbnailHeight = Math.Clamp(request.VideoThumbnailHeight, 45, 720);
        settings.VideoThumbnailJpegQuality = Math.Clamp(request.VideoThumbnailJpegQuality, 2, 31);
        settings.VideoThumbnailSpriteColumns = Math.Clamp(request.VideoThumbnailSpriteColumns, 1, 20);
        settings.VideoThumbnailConcurrency = Math.Clamp(request.VideoThumbnailConcurrency, 1, 16);
        settings.VideoThumbnailUseHardwareDecode = request.VideoThumbnailUseHardwareDecode;

        settings.FolderWatcherProviderId = request.FolderWatcherProviderId;
        settings.FolderWatcherPollingInterval = request.FolderWatcherPollingInterval;
        settings.LocalMediaScannerProviderId = request.LocalMediaScannerProviderId;
        settings.EnableTrashAutoPurge = request.EnableTrashAutoPurge;
        settings.MissingMediaRetentionDays = Math.Clamp(request.MissingMediaRetentionDays, 1, 3650);
        settings.ResolveMovieTvdbIds = request.ResolveMovieTvdbIds;
        settings.MetadataLanguage = string.IsNullOrWhiteSpace(request.MetadataLanguage) ? "eng" : request.MetadataLanguage;
        settings.RegistrationMode = (RegistrationMode)request.RegistrationMode;
        settings.InternetUploadSpeedMbps = request.InternetUploadSpeedMbps;
        settings.MaxRemoteStreamBitrateMbps = request.MaxRemoteStreamBitrateMbps;
        settings.StreamingProfile = (StreamingProfile)request.StreamingProfile;
        settings.TranscodeQuality = request.TranscodeQuality;
        settings.TranscoderTempDirectory = string.IsNullOrWhiteSpace(request.TranscoderTempDirectory) ? "/transcode" : request.TranscoderTempDirectory;
        settings.BackgroundX264Preset = request.BackgroundX264Preset;
        settings.EnableHdrToneMapping = request.EnableHdrToneMapping;
        settings.DisableVideoTranscoding = request.DisableVideoTranscoding;
        settings.UseHardwareAcceleration = request.UseHardwareAcceleration;
        settings.UseHardwareEncoding = request.UseHardwareEncoding;
        settings.EnableHevcEncoding = request.EnableHevcEncoding;
        settings.EnableHevcOptimization = request.EnableHevcOptimization;
        settings.MaxGpuTranscodes = request.MaxGpuTranscodes;
        settings.MaxCpuTranscodes = request.MaxCpuTranscodes;
        settings.MaxBackgroundTranscodes = request.MaxBackgroundTranscodes;
        settings.HardwareTranscodingDevice = string.IsNullOrWhiteSpace(request.HardwareTranscodingDevice) ? "Auto" : request.HardwareTranscodingDevice;
        settings.TranscoderThrottleBuffer = request.TranscoderThrottleBuffer;
        settings.TonemappingAlgorithm = string.IsNullOrWhiteSpace(request.TonemappingAlgorithm) ? "hable" : request.TonemappingAlgorithm;

        settings.EnableDailyMixes = request.EnableDailyMixes;
        settings.DailyMixSchedule = string.IsNullOrWhiteSpace(request.DailyMixSchedule) ? "Daily3am" : request.DailyMixSchedule;
        settings.DailyMixCount = Math.Clamp(request.DailyMixCount, 1, 12);
        settings.DailyMixSize = Math.Clamp(request.DailyMixSize, 10, 200);
        settings.DailyMixDriftPercent = Math.Clamp(request.DailyMixDriftPercent, 0, 100);
        settings.DailyMixMinPlays = Math.Max(0, request.DailyMixMinPlays);
        settings.EnableWeeklyMixes = request.EnableWeeklyMixes;

        settings.DvrStoragePath = string.IsNullOrWhiteSpace(request.DvrStoragePath) ? null : request.DvrStoragePath;
        settings.DvrMaxStorageGb = Math.Max(0, request.DvrMaxStorageGb);
        settings.DvrStorageWarningPercent = Math.Clamp(request.DvrStorageWarningPercent, 0, 100);
        settings.DvrAutoDeleteWatchedDays = Math.Max(0, request.DvrAutoDeleteWatchedDays);
        settings.DvrDefaultSeriesRetention = Math.Max(0, request.DvrDefaultSeriesRetention);
        settings.DvrNotifyOnFailure = request.DvrNotifyOnFailure;
        settings.DvrNotifyOnStorageThreshold = request.DvrNotifyOnStorageThreshold;
        settings.DvrPreRollSeconds = Math.Max(0, request.DvrPreRollSeconds);
        settings.DvrPostRollSeconds = Math.Max(0, request.DvrPostRollSeconds);
        if (Enum.TryParse<Vora.Domain.Enums.DvrConflictPolicy>(request.DvrConflictPolicy, ignoreCase: true, out var parsedPolicy))
        {
            settings.DvrConflictPolicy = parsedPolicy;
        }

        settings.TimeshiftMaxSessionHours = Math.Clamp(request.TimeshiftMaxSessionHours, 1, 48);

        await _settingsRepo.SaveChangesAsync();

        if (watcherChanged)
        {
            var watcherService = _serviceProvider.GetRequiredService<IFolderWatcherService>();
            await watcherService.RestartAllWatchersAsync();
        }
    }

    public async Task UpdatePluginSettingsAsync(string pluginId, Dictionary<string, string> settings)
    {
        foreach (var kvp in settings)
        {
            await _settingsRepo.SetPluginSettingAsync(pluginId, kvp.Key, kvp.Value);
        }
    }

    public async Task<FeatureFlagsVM> GetFeatureFlagsAsync()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        return new FeatureFlagsVM
        {
            Discover = settings.EnableDiscover,
            ForYou = settings.EnableForYou,
            ReleaseCalendar = settings.EnableReleaseCalendar,
            LiveTv = settings.EnableLiveTv,
            Dvr = settings.EnableDvr,
            InternetRadio = settings.EnableInternetRadio,
            Podcasts = settings.EnablePodcasts
        };
    }

    public async Task UpdateFeatureFlagsAsync(UpdateFeatureFlagsRequest request)
    {
        var settings = await _settingsRepo.GetSettingsForUpdateAsync();
        settings.EnableDiscover = request.Discover;
        settings.EnableForYou = request.ForYou;
        settings.EnableReleaseCalendar = request.ReleaseCalendar;
        settings.EnableLiveTv = request.LiveTv;
        settings.EnableDvr = request.Dvr;
        settings.EnableInternetRadio = request.InternetRadio;
        settings.EnablePodcasts = request.Podcasts;
        await _settingsRepo.SaveChangesAsync();
    }
}