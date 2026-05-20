using Microsoft.Extensions.Logging;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Analysis;

public interface IMediaAnalyzerManager
{
    Task TriggerMediaItemFileAnalysisAsync(Guid mediaItemId, string? name = null);
    Task TriggerLibraryFileAnalysisAsync(Guid libraryId, string? name = null);
    Task TriggerMediaItemSilenceDetectionAsync(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false);
    Task TriggerLibrarySilenceDetectionAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false);
}

public class MediaAnalyzerManager : IMediaAnalyzerManager
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IMediaAnalyzerService _analyzerService;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ITaskQueueManager _taskQueueManager;
    private readonly IClientNotifier _notifier;
    private readonly ILogger<MediaAnalyzerManager> _logger;

    public MediaAnalyzerManager(
        IMediaRepository mediaRepository,
        IMediaAnalyzerService analyzerService,
        ISystemSettingsRepository settingsRepo,
        ITaskQueueManager taskQueueManager,
        IClientNotifier notifier,
        ILogger<MediaAnalyzerManager> logger)
    {
        _mediaRepository = mediaRepository;
        _analyzerService = analyzerService;
        _settingsRepo = settingsRepo;
        _taskQueueManager = taskQueueManager;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task TriggerMediaItemFileAnalysisAsync(Guid mediaItemId, string? name = null)
    {
        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForShowAsync(mediaItemId);
            foreach (var epId in epIds) await RunFileAnalysisAsync(epId);
        }
        else if (itemType == nameof(Season))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(mediaItemId);
            foreach (var epId in epIds) await RunFileAnalysisAsync(epId);
        }
        else
        {
            await RunFileAnalysisAsync(mediaItemId);
        }
    }

    public async Task TriggerLibraryFileAnalysisAsync(Guid libraryId, string? name = null)
    {
        var mediaIds = await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId);
        foreach (var id in mediaIds)
        {
            try
            {
                await RunFileAnalysisAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File analysis failed for {MediaItemId}.", id);
            }
        }
    }

    public async Task TriggerMediaItemSilenceDetectionAsync(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false)
    {
        if (!forceOverride)
        {
            var settings = await _settingsRepo.GetSettingsAsync();
            if (isAdditionTrigger && settings.RunDetections != DetectionTrigger.OnAddition && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
            if (isScheduleTrigger && settings.RunDetections != DetectionTrigger.OnSchedule && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
        }

        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForShowAsync(mediaItemId);
            foreach (var epId in epIds) await RunMediaItemSilenceDetectionAsync(epId, forceOverride);
        }
        else if (itemType == nameof(Season))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(mediaItemId);
            foreach (var epId in epIds) await RunMediaItemSilenceDetectionAsync(epId, forceOverride);
        }
        else
        {
            await RunMediaItemSilenceDetectionAsync(mediaItemId, forceOverride);
        }
    }

    public async Task TriggerLibrarySilenceDetectionAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false)
    {
        if (!forceOverride)
        {
            var settings = await _settingsRepo.GetSettingsAsync();
            if (isAdditionTrigger && settings.RunDetections != DetectionTrigger.OnAddition && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
            if (isScheduleTrigger && settings.RunDetections != DetectionTrigger.OnSchedule && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
        }

        var mediaIds = await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId);
        foreach (var id in mediaIds)
        {
            try
            {
                await RunMediaItemSilenceDetectionAsync(id, forceOverride);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Silence detection failed for {MediaItemId}.", id);
            }
        }
    }

    private async Task RunFileAnalysisAsync(Guid mediaItemId)
    {
        var filePaths = await _mediaRepository.GetMediaFilePathsAsync(mediaItemId);
        if (filePaths == null || !filePaths.Any()) return;

        var analysisResults = new Dictionary<string, MediaAnalysisResult>();

        foreach (var path in filePaths)
        {
            var analysis = await _analyzerService.AnalyzeFileAsync(path);
            if (analysis != null) analysisResults[path] = analysis;
        }

        if (!analysisResults.Any()) return;

        var item = await _mediaRepository.GetForAnalysisAsync(mediaItemId);
        if (item == null) return;

        bool isPrimaryFile = true;

        foreach (var part in item.MediaParts)
        {
            if (!analysisResults.TryGetValue(part.FilePath, out var analysis)) continue;

            if (isPrimaryFile)
            {
                if (analysis.Duration != null && !item.IsLocked("Duration"))
                {
                    item.Analysis ??= new MediaItemAnalysis { MediaItemId = item.Id };

                    item.Analysis.Duration = analysis.Duration;
                }
            }

            part.FileSizeBytes = analysis.FileSizeBytes;
            part.OverallBitrate = analysis.OverallBitrate;
            part.Duration = analysis.Duration;

            var incomingVideo = analysis.VideoTracks.Select(v => new MediaVideoTrack
            { StreamIndex = v.StreamIndex, Codec = v.Codec, Profile = v.Profile, HdrType = v.HdrType, BitDepth = v.BitDepth, Bitrate = v.Bitrate, IsDefault = v.IsDefault }).ToList();

            var incomingAudio = analysis.AudioTracks.Select(a => new MediaAudioTrack
            { StreamIndex = a.StreamIndex, Codec = a.Codec, Language = a.Language, Channels = a.Channels, Title = a.Title, IsDefault = a.IsDefault }).ToList();

            var incomingSubtitles = analysis.SubtitleTracks.Select(s => new MediaSubtitleTrack
            { StreamIndex = s.StreamIndex, Codec = s.Codec, Language = s.Language, Title = s.Title, IsDefault = s.IsDefault, IsForced = s.IsForced }).ToList();

            await _mediaRepository.SyncMediaTracksAsync(part.Id, incomingVideo, incomingAudio, incomingSubtitles);

            isPrimaryFile = false;
        }

        await _mediaRepository.UpdateMediaItemAsync(item);

        await _notifier.NotifyMediaAnalysisUpdatedAsync(mediaItemId);
    }

    private async Task RunMediaItemSilenceDetectionAsync(Guid mediaItemId, bool forceOverride = false)
    {
        if (!forceOverride)
        {
            var isDetectionEnabled = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.Library.EnableIntroDetection);
            if (!isDetectionEnabled) return;
        }

        var filePaths = await _mediaRepository.GetMediaFilePathsAsync(mediaItemId);
        if (filePaths == null || !filePaths.Any()) return;

        var primaryPath = filePaths.First();
        var analysis = await _analyzerService.AnalyzeSilenceDetectionsAsync(primaryPath);

        if (analysis.IntroStart == null && analysis.CreditsStart == null) return;

        await _mediaRepository.UpdateSilenceDetectionsAsync(
                    mediaItemId,
                    analysis.IntroStart,
                    analysis.IntroEnd,
                    analysis.CreditsStart,
                    analysis.Duration
                );
    }
}
