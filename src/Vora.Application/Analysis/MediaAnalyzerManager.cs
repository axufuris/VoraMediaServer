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
    Task TriggerMediaItemFileAnalysisAsync(Guid mediaItemId, string? name = null, CancellationToken cancellationToken = default);
    Task AnalyzeMediaFileAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task AnalyzeMediaItemMarkersAsync(Guid mediaItemId, bool isEpisode, bool forceOverride, CancellationToken cancellationToken = default);
    Task TriggerLibraryFileAnalysisAsync(Guid libraryId, string? name = null, CancellationToken cancellationToken = default);
    Task TriggerMediaItemSilenceDetectionAsync(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default);
    Task TriggerLibrarySilenceDetectionAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default);
}

public class MediaAnalyzerManager : IMediaAnalyzerManager
{
    // Each unit's FFmpeg pass is a full-file decode. Running six of those at once
    // pegs every core; capped low so an analyze run leaves the box responsive.
    private const int AnalysisParallelism = 2;

    // Buffers added to the decode windows so a black/silence gap straddling a
    // window edge is still captured whole before the marker assembler reads it.
    private const double HeadWindowMarginSeconds = 120;
    private const double TailWindowMarginSeconds = 60;

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

    public async Task TriggerMediaItemFileAnalysisAsync(Guid mediaItemId, string? name = null, CancellationToken cancellationToken = default)
    {
        var itemType = await _mediaRepository.GetProjectedAsync(mediaItemId, m => m.GetType().Name);

        if (itemType == nameof(TvShow))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForShowAsync(mediaItemId);
            foreach (var epId in epIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunFileAnalysisAsync(epId, cancellationToken);
            }
        }
        else if (itemType == nameof(Season))
        {
            var epIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(mediaItemId);
            foreach (var epId in epIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunFileAnalysisAsync(epId, cancellationToken);
            }
        }
        else
        {
            await RunFileAnalysisAsync(mediaItemId, cancellationToken);
        }
    }

    public async Task TriggerLibraryFileAnalysisAsync(Guid libraryId, string? name = null, CancellationToken cancellationToken = default)
    {
        var mediaIds = (await _mediaRepository.GetAllMediaItemIdsByLibraryAsync(libraryId)).ToList();
        var titles = await _mediaRepository.GetDisplayTitlesByIdsAsync(mediaIds);
        var total = mediaIds.Count;
        var done = 0;

        // ffprobe is slow, so probe several files at once. Each item runs in its
        // own scope (its own DbContext + analyzer) so parallel probes don't share
        // a context. The part-level skip guard means an already-analyzed library
        // costs only a cheap file-size check per part.
        var parallelism = AnalysisParallelism;
        await Parallel.ForEachAsync(
            mediaIds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (id, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IMediaAnalyzerManager>();
                    await analyzer.AnalyzeMediaFileAsync(id, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
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

    public async Task TriggerMediaItemSilenceDetectionAsync(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();
                await RunSeasonSilenceDetectionAsync(seasonId, settings, forceOverride, cancellationToken);
            }
        }
        else if (itemType == nameof(Season))
        {
            await RunSeasonSilenceDetectionAsync(mediaItemId, settings, forceOverride, cancellationToken);
        }
        else
        {
            await RunMediaItemSilenceDetectionAsync(mediaItemId, settings, isEpisode: false, forceOverride, cancellationToken);
        }
    }

    public async Task TriggerLibrarySilenceDetectionAsync(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isAdditionTrigger = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            try
            {
                _progress.Report($"Detecting intro/credit markers — {ProgressTitle(titles, id)} ({count}/{total})");
                await TriggerMediaItemSilenceDetectionAsync(id, forceOverride: forceOverride, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Silence detection failed for {MediaItemId}.", id);
            }
        }
    }

    private async Task RunFileAnalysisAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();

            var analysis = await _analyzerService.AnalyzeFileAsync(part.FilePath, cancellationToken);
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

    public Task AnalyzeMediaFileAsync(Guid mediaItemId, CancellationToken cancellationToken = default) => RunFileAnalysisAsync(mediaItemId, cancellationToken);

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

    public async Task AnalyzeMediaItemMarkersAsync(Guid mediaItemId, bool isEpisode, bool forceOverride, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        await RunMediaItemSilenceDetectionAsync(mediaItemId, settings, isEpisode, forceOverride, cancellationToken);
    }

    private async Task RunSeasonSilenceDetectionAsync(Guid seasonId, ServerSetting settings, bool forceOverride, CancellationToken cancellationToken = default)
    {
        var episodeIds = await _mediaRepository.GetEpisodeIdsForSeasonAsync(seasonId);
        if (episodeIds.Count == 0) return;

        // Each episode's FFmpeg pass is independent and CPU-bound, so run several
        // at once — each in its own DI scope (own DbContext) so parallel writes
        // don't share a context — then finalize the season once they've all
        // committed their markers. Mirrors the file-analysis/overlay fan-out.
        var parallelism = AnalysisParallelism;
        await Parallel.ForEachAsync(
            episodeIds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (epId, ct) =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IMediaAnalyzerManager>();
                    await analyzer.AnalyzeMediaItemMarkersAsync(epId, isEpisode: true, forceOverride, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Episode silence detection failed for {EpisodeId}.", epId);
                }
            });

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await FinalizeSeasonMarkersAsync(seasonId, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Season marker finalization failed for {SeasonId}.", seasonId);
        }
    }

    // The marker assembler only reads gaps from the intro/recap head window and
    // the credits tail (>= CreditsSearchStartFraction of runtime); the middle is
    // decoded and thrown away. Return the two decode windows so the analyzer can
    // skip that middle. Null when they'd overlap (short item) → one full pass.
    private static (double? HeadEnd, double? TailStart) ComputeDecodeWindows(TimeSpan duration)
    {
        var headEnd = MarkerAssembler.IntroWindow.TotalSeconds + HeadWindowMarginSeconds;
        var tailStart = duration.TotalSeconds * MarkerAssembler.CreditsSearchStartFraction - TailWindowMarginSeconds;

        if (tailStart <= headEnd) return (null, null);
        return (headEnd, tailStart);
    }

    private async Task RunMediaItemSilenceDetectionAsync(Guid mediaItemId, ServerSetting settings, bool isEpisode, bool forceOverride, CancellationToken cancellationToken = default)
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

        // Paths + duration + stinger flags in one round-trip instead of three.
        // Duration is needed up front now (to size the decode windows and to bail
        // before any FFmpeg when it's missing, rather than decoding then discarding).
        var inputs = await _mediaRepository.GetSilenceDetectionInputsAsync(mediaItemId);
        if (inputs == null || inputs.FilePaths.Count == 0) return;

        var duration = inputs.Duration;
        if (duration == null || duration == TimeSpan.Zero)
        {
            _logger.LogWarning("Skipping marker assembly for {MediaItemId}: no duration available. Run file analysis first.", mediaItemId);
            return;
        }

        var primaryPath = inputs.FilePaths[0];

        var meanDb = await _analyzerService.ProbeMeanVolumeDbAsync(primaryPath, cancellationToken);
        var threshold = meanDb.HasValue
            ? meanDb.Value + settings.SilenceThresholdOffsetDb
            : -40d;
        var minSilence = isEpisode ? settings.SilenceMinDurationEpisodeSec : settings.SilenceMinDurationMovieSec;

        var (headEnd, tailStart) = ComputeDecodeWindows(duration.Value);

        var parameters = new SilenceDetectionParameters
        {
            NoiseThresholdDb = threshold,
            MinSilenceDurationSec = minSilence,
            MinBlackFrameDurationSec = settings.BlackFrameMinDurationSec,
            HeadWindowEndSeconds = headEnd,
            TailWindowStartSeconds = tailStart
        };

        var detection = await _analyzerService.AnalyzeSilenceDetectionsAsync(primaryPath, parameters, cancellationToken);

        // If cancelled mid-run the FFmpeg process was killed, so the detection is
        // partial — don't persist markers from it.
        cancellationToken.ThrowIfCancellationRequested();

        var assembled = _markerAssembler.Assemble(new MarkerAssemblerInput
        {
            Duration = duration.Value,
            SilenceIntervals = detection.SilenceIntervals,
            BlackIntervals = detection.BlackIntervals,
            ExpectsMidCreditsStinger = inputs.HasMidCreditsStinger,
            ExpectsPostCreditsStinger = inputs.HasPostCreditsStinger,
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
