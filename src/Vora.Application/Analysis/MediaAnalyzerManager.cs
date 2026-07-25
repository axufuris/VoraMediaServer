using Microsoft.Extensions.Logging;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
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
    private readonly IMarkerAssembler _markerAssembler;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ITaskQueueManager _taskQueueManager;
    private readonly IClientNotifier _notifier;
    private readonly ILogger<MediaAnalyzerManager> _logger;

    public MediaAnalyzerManager(
        IMediaRepository mediaRepository,
        IMediaAnalyzerService analyzerService,
        IMarkerAssembler markerAssembler,
        ISystemSettingsRepository settingsRepo,
        ITaskQueueManager taskQueueManager,
        IClientNotifier notifier,
        ILogger<MediaAnalyzerManager> logger)
    {
        _mediaRepository = mediaRepository;
        _analyzerService = analyzerService;
        _markerAssembler = markerAssembler;
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
        var settings = await _settingsRepo.GetSettingsAsync();
        if (!forceOverride)
        {
            if (isAdditionTrigger && settings.RunDetections != DetectionTrigger.OnAddition && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
            if (isScheduleTrigger && settings.RunDetections != DetectionTrigger.OnSchedule && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
        }

        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var seasonIds = await _mediaRepository.GetProjectedAsync(mediaItemId, m =>
                ((TvShow)m).Seasons.Select(s => s.Id).ToList());
            seasonIds ??= new List<Guid>();
            foreach (var seasonId in seasonIds)
            {
                await RunSeasonSilenceDetectionAsync(seasonId, settings, forceOverride);
            }
        }
        else if (itemType == nameof(Season))
        {
            await RunSeasonSilenceDetectionAsync(mediaItemId, settings, forceOverride);
        }
        else
        {
            await RunMediaItemSilenceDetectionAsync(mediaItemId, settings, isEpisode: false, forceOverride);
        }
    }

    public async Task TriggerLibrarySilenceDetectionAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        if (!forceOverride)
        {
            if (isAdditionTrigger && settings.RunDetections != DetectionTrigger.OnAddition && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
            if (isScheduleTrigger && settings.RunDetections != DetectionTrigger.OnSchedule && settings.RunDetections != DetectionTrigger.OnAdditionAndSchedule) return;
        }

        var mediaIds = await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId);
        foreach (var id in mediaIds)
        {
            try
            {
                var itemType = await _mediaRepository.GetProjectedAsync(id, m => m.GetType().Name);
                if (itemType == nameof(Episode))
                {
                    continue;
                }
                await TriggerMediaItemSilenceDetectionAsync(id, forceOverride: forceOverride);
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

    private async Task RunSeasonSilenceDetectionAsync(Guid seasonId, ServerSetting settings, bool forceOverride)
    {
        var episodeIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(seasonId);
        if (episodeIds.Count == 0) return;

        foreach (var epId in episodeIds)
        {
            try
            {
                await RunMediaItemSilenceDetectionAsync(epId, settings, isEpisode: true, forceOverride);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Episode silence detection failed for {EpisodeId}.", epId);
            }
        }

        try
        {
            await FinalizeSeasonMarkersAsync(seasonId, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Season marker finalization failed for {SeasonId}.", seasonId);
        }
    }

    private async Task RunMediaItemSilenceDetectionAsync(Guid mediaItemId, ServerSetting settings, bool isEpisode, bool forceOverride)
    {
        if (await _mediaRepository.AreMarkersLockedAsync(mediaItemId))
        {
            _logger.LogInformation("Skipping silence detection for {MediaItemId}: markers are locked.", mediaItemId);
            return;
        }

        bool detectIntro = true, detectCredits = true;
        if (!forceOverride)
        {
            var flags = await _mediaRepository.GetProjectedAsync(mediaItemId, m => new { m.Library.EnableIntroDetection, m.Library.EnableCreditsDetection });
            detectIntro = flags?.EnableIntroDetection ?? false;
            detectCredits = flags?.EnableCreditsDetection ?? false;
            if (!detectIntro && !detectCredits) return;
        }

        var filePaths = await _mediaRepository.GetMediaFilePathsAsync(mediaItemId);
        if (filePaths == null || !filePaths.Any()) return;

        var primaryPath = filePaths.First();

        var meanDb = await _analyzerService.ProbeMeanVolumeDbAsync(primaryPath);
        var threshold = meanDb.HasValue
            ? meanDb.Value + settings.SilenceThresholdOffsetDb
            : -40d;
        var minSilence = isEpisode ? settings.SilenceMinDurationEpisodeSec : settings.SilenceMinDurationMovieSec;

        var parameters = new SilenceDetectionParameters
        {
            NoiseThresholdDb = threshold,
            MinSilenceDurationSec = minSilence,
            MinBlackFrameDurationSec = settings.BlackFrameMinDurationSec
        };

        var detection = await _analyzerService.AnalyzeSilenceDetectionsAsync(primaryPath, parameters);

        var duration = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.Analysis.Duration);
        if (duration == null || duration == TimeSpan.Zero)
        {
            _logger.LogWarning("Skipping marker assembly for {MediaItemId}: no duration available. Run file analysis first.", mediaItemId);
            return;
        }

        var (midStinger, postStinger) = await _mediaRepository.GetStingerFlagsAsync(mediaItemId);

        var assembled = _markerAssembler.Assemble(new MarkerAssemblerInput
        {
            Duration = duration.Value,
            SilenceIntervals = detection.SilenceIntervals,
            BlackIntervals = detection.BlackIntervals,
            ExpectsMidCreditsStinger = midStinger,
            ExpectsPostCreditsStinger = postStinger,
            IsEpisode = isEpisode,
            DetectIntro = detectIntro,
            DetectCredits = detectCredits
        });

        var markers = assembled.Select(m => new MediaItemMarker
        {
            MediaItemId = mediaItemId,
            Type = m.Type,
            Start = m.Start,
            End = m.End,
            Order = m.Order
        }).ToList();

        await _mediaRepository.ReplaceMarkersAsync(mediaItemId, markers);
        await _notifier.NotifyMediaAnalysisUpdatedAsync(mediaItemId);
    }

    private async Task FinalizeSeasonMarkersAsync(Guid seasonId, ServerSetting settings)
    {
        var seasonMarkers = await _mediaRepository.GetMarkersForSeasonAsync(seasonId);
        if (seasonMarkers.Count == 0) return;

        var episodesById = seasonMarkers
            .GroupBy(m => m.MediaItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tolerance = TimeSpan.FromSeconds(settings.EpisodeIntroClusterToleranceSec);
        var minAgreementPct = settings.EpisodeIntroClusterMinAgreementPct;

        var introEnds = episodesById.Values
            .Select(ms => ms.FirstOrDefault(m => m.Type == MarkerType.Intro))
            .Where(m => m != null)
            .Select(m => m!.End)
            .OrderBy(t => t)
            .ToList();

        TimeSpan? canonicalIntroEnd = ClusterMedian(introEnds, tolerance, minAgreementPct);

        var creditsStarts = episodesById.Values
            .Select(ms => ms.FirstOrDefault(m => m.Type == MarkerType.Credits))
            .Where(m => m != null)
            .Select(m => m!.Start)
            .OrderBy(t => t)
            .ToList();

        TimeSpan? canonicalCreditsStart = ClusterMedian(creditsStarts, tolerance, minAgreementPct);

        if (canonicalIntroEnd == null && canonicalCreditsStart == null) return;

        foreach (var (episodeId, markers) in episodesById)
        {
            if (await _mediaRepository.AreMarkersLockedAsync(episodeId)) continue;

            var changed = false;

            if (canonicalIntroEnd != null)
            {
                var intro = markers.FirstOrDefault(m => m.Type == MarkerType.Intro);
                if (intro != null && WithinCluster(intro.End, canonicalIntroEnd.Value, tolerance) && intro.End != canonicalIntroEnd.Value)
                {
                    intro.End = canonicalIntroEnd.Value;
                    changed = true;
                }
            }

            if (canonicalCreditsStart != null)
            {
                var credits = markers.FirstOrDefault(m => m.Type == MarkerType.Credits);
                if (credits != null && WithinCluster(credits.Start, canonicalCreditsStart.Value, tolerance) && credits.Start != canonicalCreditsStart.Value)
                {
                    credits.Start = canonicalCreditsStart.Value;
                    changed = true;
                }
            }

            if (changed)
            {
                await _mediaRepository.ReplaceMarkersAsync(episodeId, markers);
                await _notifier.NotifyMediaAnalysisUpdatedAsync(episodeId);
            }
        }
    }

    private static TimeSpan? ClusterMedian(List<TimeSpan> values, TimeSpan tolerance, int minAgreementPct)
    {
        if (values.Count == 0) return null;
        var median = values[values.Count / 2];
        var inCluster = values.Count(v => WithinCluster(v, median, tolerance));
        var pct = (inCluster * 100) / values.Count;
        return pct >= minAgreementPct ? median : (TimeSpan?)null;
    }

    private static bool WithinCluster(TimeSpan candidate, TimeSpan center, TimeSpan tolerance)
    {
        var delta = candidate > center ? candidate - center : center - candidate;
        return delta <= tolerance;
    }
}
