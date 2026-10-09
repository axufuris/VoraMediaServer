using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Application.Media.Dtos;

namespace Vora.Application.Tests.Analysis;

public class MediaAnalyzerManagerSeasonSkipTests
{
    private readonly IMediaRepository _media;
    private readonly IMediaAnalyzerService _analyzer;
    private readonly IMarkerAssembler _assembler;
    private readonly ISystemSettingsRepository _settings;
    private readonly ITaskQueueManager _queue;
    private readonly IClientNotifier _notifier;
    private readonly MediaAnalyzerManager _manager;

    public MediaAnalyzerManagerSeasonSkipTests()
    {
        _media = Substitute.For<IMediaRepository>();
        _analyzer = Substitute.For<IMediaAnalyzerService>();
        _assembler = Substitute.For<IMarkerAssembler>();
        _settings = Substitute.For<ISystemSettingsRepository>();
        _queue = Substitute.For<ITaskQueueManager>();
        _notifier = Substitute.For<IClientNotifier>();

        _settings.GetSettingsAsync().Returns(new ServerSetting
        {
            RunDetections = DetectionTrigger.OnAdditionAndSchedule
        });

        _manager = new MediaAnalyzerManager(
            _media,
            _analyzer,
            _assembler,
            new AudioIntroDetector(new AudioFingerprintComparer()),
            _settings,
            Substitute.For<Vora.Application.Streaming.ISubtitleExtractionService>(),
            Substitute.For<Vora.Application.Subtitles.IExternalSubtitleScanner>(),
            _queue,
            _notifier,
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions()),
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    private void SetupSeason(Guid seasonId, bool hasPendingWork)
    {
        _media.GetProjectedAsync(seasonId, Arg.Any<Expression<Func<MediaItem, string>>>())
            .Returns(nameof(Season));
        _media.GetEpisodeIdsForSeasonAsync(seasonId).Returns(new List<Guid> { Guid.NewGuid() });
        _media.SeasonHasPendingMarkerWorkAsync(seasonId).Returns(hasPendingWork);
        _media.GetMarkersForSeasonAsync(seasonId).Returns(new List<MediaItemMarker>());
    }

    [Fact]
    public async Task Season_with_no_pending_work_skips_the_fingerprint_pass_but_still_finalizes()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: false, cancellationToken: TestContext.Current.CancellationToken);

        await _media.Received(1).SeasonHasPendingMarkerWorkAsync(seasonId);
        await _media.DidNotReceive().GetSeasonFingerprintInputsAsync(seasonId);
        await _media.Received(1).GetMarkersForSeasonAsync(seasonId);
    }

    [Fact]
    public async Task Season_with_pending_work_runs_the_fingerprint_pass()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: true);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: false, cancellationToken: TestContext.Current.CancellationToken);

        await _media.Received(1).GetSeasonFingerprintInputsAsync(seasonId);
    }

    [Fact]
    public async Task Forced_run_does_not_consult_the_pending_gate_and_runs_the_fingerprint_pass()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true, cancellationToken: TestContext.Current.CancellationToken);

        await _media.DidNotReceiveWithAnyArgs().SeasonHasPendingMarkerWorkAsync(default);
        await _media.Received(1).GetSeasonFingerprintInputsAsync(seasonId);
    }

    private static readonly DateTime ReanalysisStarted = new(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc);

    private void SetupMovie(Guid movieId, DateTime? markersAnalyzedAt)
    {
        _media.GetProjectedAsync(movieId, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns(nameof(Movie));
        _media.GetMarkerDetectionGateAsync(movieId).Returns(new MarkerDetectionGateDto { MarkersAnalyzedAt = markersAnalyzedAt });
    }

    [Fact]
    public async Task A_resumed_re_analysis_skips_a_movie_it_already_redid()
    {
        var movieId = Guid.NewGuid();
        SetupMovie(movieId, ReanalysisStarted.AddMinutes(5));

        await _manager.TriggerMediaItemSilenceDetectionAsync(movieId, forceOverride: true, analyzedBefore: ReanalysisStarted, cancellationToken: TestContext.Current.CancellationToken);

        await _media.DidNotReceive().GetSilenceDetectionInputsAsync(movieId);
    }

    [Fact]
    public async Task A_resumed_re_analysis_redoes_a_movie_analyzed_before_it_was_asked_for()
    {
        var movieId = Guid.NewGuid();
        SetupMovie(movieId, ReanalysisStarted.AddDays(-1));

        await _manager.TriggerMediaItemSilenceDetectionAsync(movieId, forceOverride: true, analyzedBefore: ReanalysisStarted, cancellationToken: TestContext.Current.CancellationToken);

        await _media.Received(1).GetSilenceDetectionInputsAsync(movieId);
    }

    [Fact]
    public async Task A_resumed_re_analysis_skips_the_fingerprint_pass_of_a_season_it_already_redid()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: true);
        _media.SeasonHasPendingMarkerWorkAsync(seasonId, ReanalysisStarted).Returns(false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true, analyzedBefore: ReanalysisStarted, cancellationToken: TestContext.Current.CancellationToken);

        await _media.DidNotReceive().GetSeasonFingerprintInputsAsync(seasonId);
        await _media.Received(1).GetMarkersForSeasonAsync(seasonId);
    }

    [Fact]
    public async Task A_resumed_re_analysis_of_a_library_only_takes_what_it_has_not_redone()
    {
        var libraryId = Guid.NewGuid();
        _media.GetMarkerDetectionTargetIdsAsync(libraryId, ReanalysisStarted).Returns(new List<Guid>());

        await _manager.TriggerLibrarySilenceDetectionAsync(libraryId, forceOverride: true, analyzedBefore: ReanalysisStarted, cancellationToken: TestContext.Current.CancellationToken);

        await _media.Received(1).GetMarkerDetectionTargetIdsAsync(libraryId, ReanalysisStarted);
        await _media.DidNotReceive().GetTopLevelMediaItemIdsByLibraryAsync(libraryId);
    }
}
