using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class EndOfFileSegmentTests : IDisposable
{
    private const double AudioSeconds = 9.5;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-end-of-file-" + Guid.NewGuid().ToString("N"));

    public EndOfFileSegmentTests()
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

    [Fact]
    public async Task A_segment_past_the_last_video_frame_is_answered_as_the_end_without_restarting_the_transcode()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (transcoder, output) = NewTranscoder();
        var decision = EncodeDecision(AudioSeconds);
        var source = await AudioOutlastsVideoAsync(cancellationToken);

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, output, decision, cancellationToken);
            await WaitUntilFinishedAsync(transcoder, decision.MediaItemId, cancellationToken);

            (await transcoder.EnsureSegmentAvailableAsync(decision.MediaItemId, 1, cancellationToken)).Should().Be(SegmentAvailability.Ready);

            var timer = Stopwatch.StartNew();
            var pastTheEnd = await transcoder.EnsureSegmentAvailableAsync(decision.MediaItemId, 2, cancellationToken);

            pastTheEnd.Should().Be(SegmentAvailability.PastTheEnd);
            timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
            transcoder.IsMediaTranscoding(decision.MediaItemId).Should().BeFalse();
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }

    [Fact]
    public async Task A_segment_past_the_end_asked_for_while_the_transcode_runs_is_answered_as_the_end_once_it_finishes()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (transcoder, output) = NewTranscoder();
        var decision = EncodeDecision(AudioSeconds);
        var source = await AudioOutlastsVideoAsync(cancellationToken);

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, output, decision, cancellationToken);

            (await transcoder.EnsureSegmentAvailableAsync(decision.MediaItemId, 2, cancellationToken)).Should().Be(SegmentAvailability.PastTheEnd);
            transcoder.IsMediaTranscoding(decision.MediaItemId).Should().BeFalse();
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }

    [Fact]
    public async Task A_file_with_no_stored_length_is_measured_rather_than_planned_as_four_hours()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var (transcoder, output) = NewTranscoder();
        var decision = EncodeDecision(durationSeconds: 0);
        var source = await AudioOutlastsVideoAsync(cancellationToken);

        try
        {
            var playlist = await transcoder.StartTranscodeSessionAsync(source, output, decision, cancellationToken);

            decision.SourceDurationSeconds.Should().BeApproximately(AudioSeconds, 0.2);
            (await File.ReadAllLinesAsync(playlist, cancellationToken)).Count(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal)).Should().Be(3);
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }

    private (FFmpegTranscodeService Transcoder, string Output) NewTranscoder()
    {
        var output = Path.Combine(_root, "transcode");
        Directory.CreateDirectory(output);
        var repository = Substitute.For<ISystemSettingsRepository>();
        repository.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = output });
        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        return (new FFmpegTranscodeService(services, Substitute.For<IHardwareCapabilityService>(), NullLogger<FFmpegTranscodeService>.Instance), output);
    }

    private static PlaybackDecisionVM EncodeDecision(double durationSeconds) => new()
    {
        MediaItemId = Guid.NewGuid(),
        Decision = StreamingState.Transcode,
        VideoStrategy = "Transcode",
        AudioStrategy = "Copy",
        SourceAudioCodec = "aac",
        TargetVideoCodec = VideoCodec.H264,
        TargetAudioCodec = AudioCodec.Aac,
        SourceDurationSeconds = durationSeconds,
    };

    private async Task<string> AudioOutlastsVideoAsync(CancellationToken cancellationToken)
    {
        var source = Path.Combine(_root, "source.mkv");
        if (File.Exists(source)) return source;
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=7.9",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={AudioSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", source);
        return source;
    }

    private static async Task WaitUntilFinishedAsync(FFmpegTranscodeService transcoder, Guid mediaItemId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (transcoder.IsMediaTranscoding(mediaItemId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, cancellationToken);
        }
        transcoder.IsMediaTranscoding(mediaItemId).Should().BeFalse();
    }
}
