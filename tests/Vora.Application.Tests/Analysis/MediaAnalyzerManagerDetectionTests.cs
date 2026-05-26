using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Analysis;

public class MediaAnalyzerManagerDetectionTests
{
    private readonly IMediaRepository _media;
    private readonly IMediaAnalyzerService _analyzer;
    private readonly IMarkerAssembler _assembler;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskQueueManager _queue;
    private readonly IClientNotifier _notifier;
    private readonly MediaAnalyzerManager _manager;

    public MediaAnalyzerManagerDetectionTests()
    {
        _media = Substitute.For<IMediaRepository>();
        _analyzer = Substitute.For<IMediaAnalyzerService>();
        _assembler = Substitute.For<IMarkerAssembler>();
        _settings = Substitute.For<ISystemSettingsRepository>();
        _queue = Substitute.For<ITaskQueueManager>();
        _notifier = Substitute.For<IClientNotifier>();

        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            RunDetections = DetectionTrigger.OnAdditionAndSchedule,
            SilenceThresholdOffsetDb = -10,
            SilenceMinDurationMovieSec = 3,
            SilenceMinDurationEpisodeSec = 2,
            BlackFrameMinDurationSec = 1,
            EpisodeIntroClusterToleranceSec = 3,
            EpisodeIntroClusterMinAgreementPct = 60
        });

        _manager = new MediaAnalyzerManager(
            _media, _analyzer, _assembler, _settings, _queue, _notifier,
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    private void StubMovieReady(Guid id, string filePath, TimeSpan duration, double? meanDb)
    {
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Movie");
        _media.AreMarkersLockedAsync(id).Returns(false);
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, bool>>>()).Returns(true);
        _media.GetMediaFilePathsAsync(id).Returns(new List<string> { filePath });
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, TimeSpan?>>>()).Returns(duration);
        _media.GetStingerFlagsAsync(id).Returns((false, false));
        _analyzer.ProbeMeanVolumeDbAsync(filePath).Returns(meanDb);
        _analyzer.AnalyzeSilenceDetectionsAsync(filePath, Arg.Any<SilenceDetectionParameters>())
            .Returns(new MediaAnalysisResult { Duration = duration });
        _assembler.Assemble(Arg.Any<MarkerAssemblerInput>()).Returns(new List<DetectedMarker>
        {
            new() { Type = MarkerType.Credits, Start = TimeSpan.FromMinutes(80), End = duration }
        });
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_passes_probed_threshold_with_offset_to_analyzer()
    {
        var id = Guid.NewGuid();
        StubMovieReady(id, "/m/a.mkv", TimeSpan.FromMinutes(90), meanDb: -20);

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: true);

        await _analyzer.Received(1).AnalyzeSilenceDetectionsAsync("/m/a.mkv",
            Arg.Is<SilenceDetectionParameters>(p =>
                p.NoiseThresholdDb == -30 &&
                p.MinSilenceDurationSec == 3 &&
                p.MinBlackFrameDurationSec == 1));
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_falls_back_to_minus_40_threshold_when_probe_returns_null()
    {
        var id = Guid.NewGuid();
        StubMovieReady(id, "/m/a.mkv", TimeSpan.FromMinutes(90), meanDb: null);

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: true);

        await _analyzer.Received(1).AnalyzeSilenceDetectionsAsync("/m/a.mkv",
            Arg.Is<SilenceDetectionParameters>(p => p.NoiseThresholdDb == -40));
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_skips_marker_assembly_when_duration_is_null()
    {
        var id = Guid.NewGuid();
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Movie");
        _media.AreMarkersLockedAsync(id).Returns(false);
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, bool>>>()).Returns(true);
        _media.GetMediaFilePathsAsync(id).Returns(new List<string> { "/m/a.mkv" });
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, TimeSpan?>>>()).Returns((TimeSpan?)null);
        _analyzer.ProbeMeanVolumeDbAsync("/m/a.mkv").Returns(-20);
        _analyzer.AnalyzeSilenceDetectionsAsync("/m/a.mkv", Arg.Any<SilenceDetectionParameters>())
            .Returns(new MediaAnalysisResult());

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: true);

        _assembler.DidNotReceiveWithAnyArgs().Assemble(default!);
        await _media.DidNotReceive().ReplaceMarkersAsync(Arg.Any<Guid>(), Arg.Any<List<MediaItemMarker>>());
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_persists_markers_and_notifies_on_success()
    {
        var id = Guid.NewGuid();
        StubMovieReady(id, "/m/a.mkv", TimeSpan.FromMinutes(90), meanDb: -20);

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: true);

        await _media.Received(1).ReplaceMarkersAsync(id, Arg.Is<IEnumerable<MediaItemMarker>>(ms =>
            ms.Any(m => m.Type == MarkerType.Credits)));
        await _notifier.Received(1).NotifyMediaAnalysisUpdatedAsync(id);
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_skips_when_library_detection_disabled_and_not_forced()
    {
        var id = Guid.NewGuid();
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Movie");
        _media.AreMarkersLockedAsync(id).Returns(false);
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, bool>>>()).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: false);

        await _media.DidNotReceive().GetMediaFilePathsAsync(Arg.Any<Guid>());
        await _analyzer.DidNotReceiveWithAnyArgs().ProbeMeanVolumeDbAsync(string.Empty);
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_skips_when_no_file_paths_found()
    {
        var id = Guid.NewGuid();
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Movie");
        _media.AreMarkersLockedAsync(id).Returns(false);
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, bool>>>()).Returns(true);
        _media.GetMediaFilePathsAsync(id).Returns(new List<string>());

        await _manager.TriggerMediaItemSilenceDetectionAsync(id, forceOverride: true);

        await _analyzer.DidNotReceiveWithAnyArgs().ProbeMeanVolumeDbAsync(string.Empty);
        _assembler.DidNotReceiveWithAnyArgs().Assemble(default!);
    }

    [Fact]
    public async Task RunMediaItemSilenceDetectionAsync_uses_episode_silence_duration_for_episodes()
    {
        // Routed via Season → Episode path; the manager should pass the EpisodeSilence duration setting
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { episodeId });

        // Episode stubs
        StubMovieReady(episodeId, "/m/ep.mkv", TimeSpan.FromMinutes(45), meanDb: -25);
        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>());

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        await _analyzer.Received(1).AnalyzeSilenceDetectionsAsync("/m/ep.mkv",
            Arg.Is<SilenceDetectionParameters>(p => p.MinSilenceDurationSec == 2));
    }

    [Fact]
    public async Task FinalizeSeasonMarkersAsync_snaps_intro_ends_to_canonical_median_when_agreement_met()
    {
        var seasonId = Guid.NewGuid();
        var ep1 = Guid.NewGuid();
        var ep2 = Guid.NewGuid();
        var ep3 = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        // Return non-empty list so RunSeasonSilenceDetectionAsync proceeds past its guard and reaches
        // FinalizeSeasonMarkersAsync. Per-episode detection no-ops because GetMediaFilePathsAsync defaults to null.
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { ep1 });

        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>
        {
            new() { MediaItemId = ep1, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(120) },
            new() { MediaItemId = ep2, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(122) },
            new() { MediaItemId = ep3, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(123) }
        });
        _media.AreMarkersLockedAsync(Arg.Any<Guid>()).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        // Median of (120, 122, 123) is 122; tolerance is 3s, so all three fall within cluster.
        // Episode 1 (end=120) and ep3 (end=123) differ from 122 — both should be snapped to 122.
        await _media.Received().ReplaceMarkersAsync(ep1, Arg.Is<IEnumerable<MediaItemMarker>>(ms =>
            ms.Any(m => m.Type == MarkerType.Intro && m.End == TimeSpan.FromSeconds(122))));
        await _media.Received().ReplaceMarkersAsync(ep3, Arg.Is<IEnumerable<MediaItemMarker>>(ms =>
            ms.Any(m => m.Type == MarkerType.Intro && m.End == TimeSpan.FromSeconds(122))));
    }

    [Fact]
    public async Task FinalizeSeasonMarkersAsync_does_not_snap_when_agreement_below_threshold()
    {
        var seasonId = Guid.NewGuid();
        var ep1 = Guid.NewGuid();
        var ep2 = Guid.NewGuid();
        var ep3 = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { ep1 });

        // Spread is wide: 60, 120, 180. Median is 120, tolerance is 3s → only 1/3 = 33% in cluster < 60% threshold.
        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>
        {
            new() { MediaItemId = ep1, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(60) },
            new() { MediaItemId = ep2, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(120) },
            new() { MediaItemId = ep3, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(180) }
        });
        _media.AreMarkersLockedAsync(Arg.Any<Guid>()).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        // No snap should occur — none of the episodes' markers should be persisted via finalize.
        await _media.DidNotReceive().ReplaceMarkersAsync(ep1, Arg.Any<IEnumerable<MediaItemMarker>>());
        await _media.DidNotReceive().ReplaceMarkersAsync(ep2, Arg.Any<IEnumerable<MediaItemMarker>>());
        await _media.DidNotReceive().ReplaceMarkersAsync(ep3, Arg.Any<IEnumerable<MediaItemMarker>>());
    }

    [Fact]
    public async Task FinalizeSeasonMarkersAsync_respects_marker_locks_per_episode()
    {
        var seasonId = Guid.NewGuid();
        var lockedEp = Guid.NewGuid();
        var unlockedEp = Guid.NewGuid();
        var thirdEp = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { unlockedEp });
        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>
        {
            new() { MediaItemId = lockedEp, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(120) },
            new() { MediaItemId = unlockedEp, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(123) },
            new() { MediaItemId = thirdEp, Type = MarkerType.Intro, Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(124) }
        });
        _media.AreMarkersLockedAsync(lockedEp).Returns(true);
        _media.AreMarkersLockedAsync(unlockedEp).Returns(false);
        _media.AreMarkersLockedAsync(thirdEp).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        await _media.DidNotReceive().ReplaceMarkersAsync(lockedEp, Arg.Any<IEnumerable<MediaItemMarker>>());
    }

    [Fact]
    public async Task FinalizeSeasonMarkersAsync_snaps_credits_starts_to_canonical_median()
    {
        var seasonId = Guid.NewGuid();
        var ep1 = Guid.NewGuid();
        var ep2 = Guid.NewGuid();
        var ep3 = Guid.NewGuid();

        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns("Season");
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { ep1 });
        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>
        {
            new() { MediaItemId = ep1, Type = MarkerType.Credits, Start = TimeSpan.FromMinutes(40), End = TimeSpan.FromMinutes(45) },
            new() { MediaItemId = ep2, Type = MarkerType.Credits, Start = TimeSpan.FromMinutes(40).Add(TimeSpan.FromSeconds(2)), End = TimeSpan.FromMinutes(45) },
            new() { MediaItemId = ep3, Type = MarkerType.Credits, Start = TimeSpan.FromMinutes(40).Add(TimeSpan.FromSeconds(3)), End = TimeSpan.FromMinutes(45) }
        });
        _media.AreMarkersLockedAsync(Arg.Any<Guid>()).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        // Median = 40m+2s = 2402s. ep1 (40m=2400s) and ep3 (40m+3s=2403s) should snap; ep2 already at median.
        await _media.Received().ReplaceMarkersAsync(ep1, Arg.Is<IEnumerable<MediaItemMarker>>(ms =>
            ms.Any(m => m.Type == MarkerType.Credits && m.Start == TimeSpan.FromMinutes(40).Add(TimeSpan.FromSeconds(2)))));
        await _media.Received().ReplaceMarkersAsync(ep3, Arg.Is<IEnumerable<MediaItemMarker>>(ms =>
            ms.Any(m => m.Type == MarkerType.Credits && m.Start == TimeSpan.FromMinutes(40).Add(TimeSpan.FromSeconds(2)))));
    }
}
