using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Tests.Transcoding;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests.Analysis;

public sealed class RecapDetectionEndToEndTests : IDisposable
{
    private static readonly TimeSpan EpisodeLength = TimeSpan.FromMinutes(25);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-recap-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();

    public RecapDetectionEndToEndTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_fingerprinted_episode_with_a_previously_on_line_gets_a_recap_that_ends_at_the_black_before_its_cold_open()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var episodeId = Guid.NewGuid();
        var episode = await EpisodeAsync(cancellationToken);

        _media.GetMarkerDetectionGateAsync(episodeId).Returns(new MarkerDetectionGateDto { EnableIntroDetection = true, EnableCreditsDetection = true });
        _media.GetSilenceDetectionInputsAsync(episodeId).Returns(new SilenceDetectionInputsDto { FilePaths = [episode], Duration = EpisodeLength });
        _media.GetSubtitleExtractionTargetsForItemAsync(episodeId).Returns(new List<SubtitleExtractionTargetDto>
        {
            new() { MediaItemId = episodeId, FilePath = episode, Tracks = [new() { StreamIndex = 2, Codec = "subrip", Language = "eng" }] }
        });
        var fingerprintIntro = new DetectedMarker { Type = MarkerType.Intro, Start = TimeSpan.FromSeconds(90), End = TimeSpan.FromSeconds(120) };

        await Manager().AnalyzeMediaItemMarkersAsync(episodeId, isEpisode: true, forceOverride: true, fingerprintIntro, cancellationToken: cancellationToken);

        var saved = _media.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMediaRepository.ReplaceMarkersAsync))
            .Select(call => call.GetArguments()[1])
            .OfType<IEnumerable<MediaItemMarker>>()
            .Single()
            .ToList();
        var recap = saved.Single(marker => marker.Type == MarkerType.Recap);
        recap.Start.Should().Be(TimeSpan.Zero);
        recap.End.TotalSeconds.Should().BeApproximately(62.5, 0.5);
        saved.Single(marker => marker.Type == MarkerType.Intro).Start.Should().Be(TimeSpan.FromSeconds(90));
    }

    private MediaAnalyzerManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting
        {
            UseHardwareAcceleration = false,
            SilenceThresholdOffsetDb = -12,
            SilenceMinDurationEpisodeSec = 1,
            BlackFrameMinDurationSec = 0.5,
        });
        var scopes = Substitute.For<IServiceScopeFactory>();

        return new MediaAnalyzerManager(
            _media,
            new FFmpegAnalyzerService(NullLogger<FFmpegAnalyzerService>.Instance),
            new MarkerAssembler(),
            new AudioIntroDetector(new AudioFingerprintComparer()),
            settings,
            Substitute.For<IHardwareCapabilityService>(),
            Substitute.For<ISubtitleExtractionService>(),
            Substitute.For<IExternalSubtitleScanner>(),
            Substitute.For<ITaskQueueManager>(),
            Substitute.For<IClientNotifier>(),
            new NullTaskProgressReporter(),
            scopes,
            Options.Create(new StoragePathsOptions()),
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    private async Task<string> EpisodeAsync(CancellationToken cancellationToken)
    {
        var srt = Path.Combine(_folder, "episode.en.srt");
        await File.WriteAllTextAsync(srt, """
            1
            00:00:01,000 --> 00:00:03,500
            Previously on The Terminal List...

            2
            00:00:30,000 --> 00:00:32,000
            We go in at dawn.

            3
            00:01:05,000 --> 00:01:07,000
            Where is everybody?

            """, cancellationToken);

        var episode = Path.Combine(_folder, "episode.mkv");
        var rest = (EpisodeLength.TotalSeconds - 62.5).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10:duration=60",
            "-f", "lavfi", "-i", "color=black:size=160x90:rate=10:duration=2.5",
            "-f", "lavfi", "-i", $"testsrc2=size=160x90:rate=10:duration={rest}",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=60",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=48000:duration=2.5",
            "-f", "lavfi", "-i", $"sine=frequency=330:sample_rate=48000:duration={rest}",
            "-i", srt,
            "-filter_complex", "[0:v][3:a][1:v][4:a][2:v][5:a]concat=n=3:v=1:a=1[v][a]",
            "-map", "[v]", "-map", "[a]", "-map", "6:s",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", "srt", episode);
        return episode;
    }
}
