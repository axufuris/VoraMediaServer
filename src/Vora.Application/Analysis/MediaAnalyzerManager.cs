using Microsoft.Extensions.DependencyInjection;
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
    Task AnalyzeMediaFileAsync(Guid mediaItemId);
    Task AnalyzeMediaItemMarkersAsync(Guid mediaItemId, bool isEpisode, bool forceOverride);
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
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaAnalyzerManager> _logger;

    public MediaAnalyzerManager(
        IMediaRepository mediaRepository,
        IMediaAnalyzerService analyzerService,
        IMarkerAssembler markerAssembler,
        ISystemSettingsRepository settingsRepo,
        ITaskQueueManager taskQueueManager,
        IClientNotifier notifier,
        Vora.Plugins.Interfaces.ITaskProgressReporter progress,
        IServiceScopeFactory scopeFactory,
        ILogger<MediaAnalyzerManager> logger)
    {
        _mediaRepository = mediaRepository;
        _analyzerService = analyzerService;
        _markerAssembler = markerAssembler;
        _settingsRepo = settingsRepo;
        _taskQueueManager = taskQueueManager;
        _notifier = notifier;
        _progress = progress;
        _scopeFactory = scopeFactory;
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
        var mediaIds = (await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId)).ToList();
        var titles = await _mediaRepository.GetDisplayTitlesByIdsAsync(mediaIds);
        var total = mediaIds.Count;
        var done = 0;

        // ffprobe is slow, so probe several files at once. Each item runs in its
        // own scope (its own DbContext + analyzer) so parallel probes don't share
        // a context. The part-level skip guard means an already-analyzed library
        // costs only a cheap file-size check per part.
        var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
        await Parallel.ForEachAsync(
            mediaIds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (id, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IMediaAnalyzerManager>();
                    await analyzer.AnalyzeMediaFileAsync(id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "File analysis failed for {MediaItemId}.", id);
                }

                var n = Interlocked.Increment(ref done);
                _progress.Report($"Analyzing media — {ProgressTitle(titles, id)} ({n}/{total})");
            });
    }

    private static string ProgressTitle(IReadOnlyDictionary<Guid, string> titles, Guid id) =>
        titles.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t) ? t : "…";

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

        // Only iterate the library's top-level items (shows + movies). Seasons and
        // episodes are reached by recursing into each show, so pulling the whole
        // library here meant a per-item type round-trip for every episode just to
        // skip it — and every season's episodes were then detected twice (once via
        // the show, once via the season). For a large TV library that's tens of
        // thousands of wasted queries before any real work starts.
        var mediaIds = await _mediaRepository.GetTopLevelMediaItemIdsByLibraryAsync(libraryId);
        if (mediaIds.Count == 0) return;

        // Both marker toggles off → there's nothing to detect. Bail before the
        // per-item loop so the task doesn't iterate the library (and show
        // "Detecting intro/credit markers …") when the library has detection
        // disabled. The per-item path also checks these flags, but only after
        // loading each item — this skips the work and the misleading progress.
        var markerFlags = await _mediaRepository.GetProjectedAsync(mediaIds[0],
            m => new { m.Library.EnableIntroDetection, m.Library.EnableCreditsDetection });
        if (markerFlags != null && !markerFlags.EnableIntroDetection && !markerFlags.EnableCreditsDetection)
        {
            _logger.LogInformation("Skipping intro/credit marker detection for library {LibraryId}: both toggles are disabled.", libraryId);
            return;
        }

        var titles = await _mediaRepository.GetDisplayTitlesByIdsAsync(mediaIds);
        var total = mediaIds.Count;
        var count = 0;

        foreach (var id in mediaIds)
        {
            count++;
            try
            {
                _progress.Report($"Detecting intro/credit markers — {ProgressTitle(titles, id)} ({count}/{total})");
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
        var item = await _mediaRepository.GetForAnalysisAsync(mediaItemId);
        if (item == null || item.MediaParts.Count == 0) return;

        // Part-level skip guard: only (re)probe parts that were never analyzed or
        // whose file changed on disk (size differs). An unchanged, already-
        // analyzed library isn't re-probed on every scan, but an added or
        // replaced file still gets analyzed.
        var partsToAnalyze = item.MediaParts.Where(PartNeedsAnalysis).ToHashSet();
        if (partsToAnalyze.Count == 0) return;

        var primaryPart = item.MediaParts.First();

        foreach (var part in item.MediaParts)
        {
            if (!partsToAnalyze.Contains(part)) continue;

            var analysis = await _analyzerService.AnalyzeFileAsync(part.FilePath);
            if (analysis == null) continue;

            if (analysis.Duration != null && !item.IsLocked("Duration") && (part == primaryPart || item.Analysis?.Duration == null))
            {
                item.Analysis ??= new MediaItemAnalysis { MediaItemId = item.Id };
                item.Analysis.Duration = analysis.Duration;
            }

            part.FileSizeBytes = analysis.FileSizeBytes;
            part.OverallBitrate = analysis.OverallBitrate;
            part.Duration = analysis.Duration;
            part.LastAnalyzedAt = DateTime.UtcNow;

            var incomingVideo = analysis.VideoTracks.Select(v => new MediaVideoTrack
            { StreamIndex = v.StreamIndex, Codec = v.Codec, Profile = v.Profile, HdrType = v.HdrType, BitDepth = v.BitDepth, Bitrate = v.Bitrate, IsDefault = v.IsDefault }).ToList();

            var incomingAudio = analysis.AudioTracks.Select(a => new MediaAudioTrack
            { StreamIndex = a.StreamIndex, Codec = a.Codec, Language = a.Language, Channels = a.Channels, Title = a.Title, IsDefault = a.IsDefault }).ToList();

            var incomingSubtitles = analysis.SubtitleTracks.Select(s => new MediaSubtitleTrack
            { StreamIndex = s.StreamIndex, Codec = s.Codec, Language = s.Language, Title = s.Title, IsDefault = s.IsDefault, IsForced = s.IsForced }).ToList();

            await _mediaRepository.SyncMediaTracksAsync(part.Id, incomingVideo, incomingAudio, incomingSubtitles);
        }

        // A part was (re)probed here (an added or replaced file — unchanged files
        // returned above), so any existing intro/credit markers are stale. Clear
        // the marker-analysis stamp so the skip-gate re-detects them next pass.
        item.MarkersAnalyzedAt = null;

        await _mediaRepository.UpdateMediaItemAsync(item);

        await _notifier.NotifyMediaAnalysisUpdatedAsync(mediaItemId);
    }

    public Task AnalyzeMediaFileAsync(Guid mediaItemId) => RunFileAnalysisAsync(mediaItemId);

    private static bool PartNeedsAnalysis(MediaPart part)
    {
        if (part.LastAnalyzedAt == null) return true;
        try
        {
            var info = new FileInfo(part.FilePath);
            return !info.Exists || info.Length != part.FileSizeBytes;
        }
        catch
        {
            return true;
        }
    }

    public async Task AnalyzeMediaItemMarkersAsync(Guid mediaItemId, bool isEpisode, bool forceOverride)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        await RunMediaItemSilenceDetectionAsync(mediaItemId, settings, isEpisode, forceOverride);
    }

    private async Task RunSeasonSilenceDetectionAsync(Guid seasonId, ServerSetting settings, bool forceOverride)
    {
        var episodeIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(seasonId);
        if (episodeIds.Count == 0) return;

        // Each episode's FFmpeg pass is independent and CPU-bound, so run several
        // at once — each in its own DI scope (own DbContext) so parallel writes
        // don't share a context — then finalize the season once they've all
        // committed their markers. Mirrors the file-analysis/overlay fan-out.
        var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
        await Parallel.ForEachAsync(
            episodeIds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (epId, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IMediaAnalyzerManager>();
                    await analyzer.AnalyzeMediaItemMarkersAsync(epId, isEpisode: true, forceOverride);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Episode silence detection failed for {EpisodeId}.", epId);
                }
            });

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
        // One round-trip for the whole skip decision (lock + already-analyzed +
        // per-library toggles) instead of three sequential queries per item —
        // this runs once for every episode in the library, so the round-trip
        // count matters.
        var gate = await _mediaRepository.GetMarkerDetectionGateAsync(mediaItemId);
        if (gate == null) return;

        if (gate.AreMarkersLocked)
        {
            _logger.LogInformation("Skipping silence detection for {MediaItemId}: markers are locked.", mediaItemId);
            return;
        }

        bool detectIntro = true, detectCredits = true;
        if (!forceOverride)
        {
            // Skip items already analyzed: a non-forced "Analyze library" or the
            // nightly run only needs to process new/never-analyzed items, so the
            // FFmpeg passes aren't re-run over the whole library every time. A
            // manual per-item/library force re-runs by passing forceOverride.
            if (gate.MarkersAnalyzedAt != null) return;

            detectIntro = gate.EnableIntroDetection;
            detectCredits = gate.EnableCreditsDetection;
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
