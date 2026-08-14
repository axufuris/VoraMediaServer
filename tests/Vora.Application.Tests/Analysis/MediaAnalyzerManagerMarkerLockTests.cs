using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Analysis;

public class MediaAnalyzerManagerMarkerLockTests
{
    private readonly IMediaRepository _media;
    private readonly IMediaAnalyzerService _analyzer;
    private readonly IMarkerAssembler _assembler;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskQueueManager _queue;
    private readonly IClientNotifier _notifier;
    private readonly MediaAnalyzerManager _manager;

    public MediaAnalyzerManagerMarkerLockTests()
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
            SilenceMinDurationMovieSec = 2,
            SilenceMinDurationEpisodeSec = 2,
            BlackFrameMinDurationSec = 1
        });

        _manager = new MediaAnalyzerManager(
            _media,
            _analyzer,
            _assembler,
            _settings,
            _queue,
            _notifier,
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    [Fact]
    public async Task TriggerMediaItemSilenceDetectionAsync_bails_when_markers_locked()
    {
        var mediaItemId = Guid.NewGuid();

        _media.GetProjectedAsync(mediaItemId, Arg.Any<Expression<Func<MediaItem, string>>>())
            .Returns("Movie");
        _media.GetMarkerDetectionGateAsync(mediaItemId).Returns(new MarkerDetectionGateDto
        { LockedFields = new List<string> { "Markers" } });

        await _manager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, forceOverride: true);

        await _analyzer.DidNotReceiveWithAnyArgs().ProbeMeanVolumeDbAsync(default!);
        await _media.DidNotReceive().GetMediaFilePathsAsync(Arg.Any<Guid>());
        await _media.DidNotReceive().ReplaceMarkersAsync(Arg.Any<Guid>(), Arg.Any<List<MediaItemMarker>>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyMediaAnalysisUpdatedAsync(default);
    }

    [Fact]
    public async Task TriggerMediaItemSilenceDetectionAsync_proceeds_to_probe_when_unlocked()
    {
        var mediaItemId = Guid.NewGuid();

        _media.GetProjectedAsync(mediaItemId, Arg.Any<Expression<Func<MediaItem, string>>>())
            .Returns("Movie");
        _media.GetMarkerDetectionGateAsync(mediaItemId).Returns(new MarkerDetectionGateDto
        { EnableIntroDetection = true, EnableCreditsDetection = true });
        _media.GetSilenceDetectionInputsAsync(mediaItemId).Returns(new SilenceDetectionInputsDto
        { FilePaths = new List<string> { "/media/movies/Sample.mkv" }, Duration = TimeSpan.FromMinutes(90) });
        _analyzer.AnalyzeSilenceDetectionsAsync("/media/movies/Sample.mkv", Arg.Any<SilenceDetectionParameters>(), Arg.Any<CancellationToken>())
            .Returns(new MediaAnalysisResult());
        _assembler.Assemble(Arg.Any<MarkerAssemblerInput>()).Returns(new List<DetectedMarker>());

        await _manager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, forceOverride: true);

        await _analyzer.Received(1).ProbeMeanVolumeDbAsync("/media/movies/Sample.mkv", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TriggerMediaItemSilenceDetectionAsync_skips_when_trigger_gate_blocks()
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            RunDetections = DetectionTrigger.Never
        });

        var mediaItemId = Guid.NewGuid();

        await _manager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, forceOverride: false, isAdditionTrigger: true);

        await _media.DidNotReceiveWithAnyArgs().GetMarkerDetectionGateAsync(default);
        await _analyzer.DidNotReceiveWithAnyArgs().ProbeMeanVolumeDbAsync(default!);
    }

    [Fact]
    public async Task TriggerMediaItemSilenceDetectionAsync_passes_trigger_gate_with_forceOverride()
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            RunDetections = DetectionTrigger.Never
        });

        var mediaItemId = Guid.NewGuid();
        _media.GetProjectedAsync(mediaItemId, Arg.Any<Expression<Func<MediaItem, string>>>())
            .Returns("Movie");
        _media.GetMarkerDetectionGateAsync(mediaItemId).Returns(new MarkerDetectionGateDto
        { LockedFields = new List<string> { "Markers" } });

        await _manager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, forceOverride: true);

        await _media.Received(1).GetMarkerDetectionGateAsync(mediaItemId);
    }

    [Theory]
    [InlineData(DetectionTrigger.OnAddition, true, false)]
    [InlineData(DetectionTrigger.OnAdditionAndSchedule, true, false)]
    [InlineData(DetectionTrigger.OnSchedule, false, true)]
    [InlineData(DetectionTrigger.OnAdditionAndSchedule, false, true)]
    public async Task TriggerMediaItemSilenceDetectionAsync_runs_when_setting_matches_trigger(
        DetectionTrigger setting, bool isAddition, bool isSchedule)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { RunDetections = setting });

        var mediaItemId = Guid.NewGuid();
        _media.GetProjectedAsync(mediaItemId, Arg.Any<Expression<Func<MediaItem, string>>>())
            .Returns("Movie");
        _media.GetMarkerDetectionGateAsync(mediaItemId).Returns(new MarkerDetectionGateDto
        { LockedFields = new List<string> { "Markers" } });

        await _manager.TriggerMediaItemSilenceDetectionAsync(
            mediaItemId,
            forceOverride: false,
            isAdditionTrigger: isAddition,
            isScheduleTrigger: isSchedule);

        await _media.Received(1).GetMarkerDetectionGateAsync(mediaItemId);
    }
}
