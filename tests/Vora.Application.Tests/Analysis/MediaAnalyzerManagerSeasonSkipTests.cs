using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

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

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: false);

        await _media.Received(1).SeasonHasPendingMarkerWorkAsync(seasonId);
        await _media.DidNotReceive().GetSeasonFingerprintInputsAsync(seasonId);
        await _media.Received(1).GetMarkersForSeasonAsync(seasonId);
    }

    [Fact]
    public async Task Season_with_pending_work_runs_the_fingerprint_pass()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: true);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: false);

        await _media.Received(1).GetSeasonFingerprintInputsAsync(seasonId);
    }

    [Fact]
    public async Task Forced_run_does_not_consult_the_pending_gate_and_runs_the_fingerprint_pass()
    {
        var seasonId = Guid.NewGuid();
        SetupSeason(seasonId, hasPendingWork: false);

        await _manager.TriggerMediaItemSilenceDetectionAsync(seasonId, forceOverride: true);

        await _media.DidNotReceiveWithAnyArgs().SeasonHasPendingMarkerWorkAsync(default);
        await _media.Received(1).GetSeasonFingerprintInputsAsync(seasonId);
    }
}
