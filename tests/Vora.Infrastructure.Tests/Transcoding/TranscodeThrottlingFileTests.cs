using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Settings;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class TranscodeThrottlingFileTests : IDisposable
{
    private static readonly FFmpegTranscodeService.TranscodeHost Jellyfin = new(HasJellyfinFfmpeg: true, CanUseGpuHdrTonemap: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-throttle-" + Guid.NewGuid().ToString("N"));

    public TranscodeThrottlingFileTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int Segments(string output, Guid mediaItemId) => Directory.GetFiles(output, $"{mediaItemId}_*.ts").Length;

    [Fact]
    public async Task A_transcode_far_ahead_of_the_player_pauses_and_picks_up_when_the_player_catches_up()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable() && FfmpegTool.IsJellyfinBuild(), FfmpegTool.JellyfinSkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = Path.Combine(_root, "source.mkv");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "900", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", source);
        var output = Path.Combine(_root, "transcode");
        Directory.CreateDirectory(output);

        var repository = Substitute.For<ISystemSettingsRepository>();
        repository.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = output, UseHardwareAcceleration = false, TranscoderThrottleBuffer = 30 });
        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        var transcoder = new FFmpegTranscodeService(services, Substitute.For<IHardwareCapabilityService>(), NullLogger<FFmpegTranscodeService>.Instance);
        var decision = new PlaybackDecisionVM
        {
            MediaItemId = Guid.NewGuid(),
            Decision = StreamingState.Transcode,
            VideoStrategy = "Transcode",
            AudioStrategy = "Copy",
            SourceAudioCodec = "aac",
            TargetVideoCodec = VideoCodec.H264,
            TargetAudioCodec = AudioCodec.Aac,
            SourceDurationSeconds = 900,
        };

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, output, decision, cancellationToken);
            transcoder.NotifySegmentServed(decision.MediaItemId, 0);
            for (var tick = 0; tick < 20; tick++)
            {
                await transcoder.ThrottleAsync(Jellyfin, cancellationToken);
                await Task.Delay(100, cancellationToken);
            }

            await Task.Delay(1000, cancellationToken);
            var whilePaused = Segments(output, decision.MediaItemId);
            await Task.Delay(2000, cancellationToken);
            Segments(output, decision.MediaItemId).Should().Be(whilePaused, "a paused transcode writes nothing");
            whilePaused.Should().BeLessThan(225, "the whole film would be 225 segments");

            transcoder.NotifySegmentServed(decision.MediaItemId, whilePaused - 1);
            await transcoder.ThrottleAsync(Jellyfin, cancellationToken);
            await Task.Delay(2000, cancellationToken);

            Segments(output, decision.MediaItemId).Should().BeGreaterThan(whilePaused, "a resumed transcode carries on");
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }
}
