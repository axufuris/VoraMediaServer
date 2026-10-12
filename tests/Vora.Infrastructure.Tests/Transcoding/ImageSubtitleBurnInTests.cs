using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Settings;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class ImageSubtitleBurnInTests : IDisposable
{
    private static readonly TimeSpan ShowAt = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HideAt = TimeSpan.FromSeconds(9);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-burn-in-" + Guid.NewGuid().ToString("N"));
    private readonly string _output;

    public ImageSubtitleBurnInTests()
    {
        _output = Path.Combine(_root, "transcode");
        Directory.CreateDirectory(_output);
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

    private FFmpegTranscodeService Transcoder()
    {
        var repository = Substitute.For<ISystemSettingsRepository>();
        repository.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = _output, UseHardwareAcceleration = false });
        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        return new FFmpegTranscodeService(services, Substitute.For<IHardwareCapabilityService>(), NullLogger<FFmpegTranscodeService>.Instance);
    }

    private async Task<string> SourceAsync(bool withEmbeddedSubtitle, CancellationToken cancellationToken)
    {
        var source = Path.Combine(_root, "source.mkv");
        List<string> args = ["-f", "lavfi", "-i", "color=black:size=320x240:rate=25", "-f", "lavfi", "-i", "sine=frequency=440"];
        if (withEmbeddedSubtitle)
        {
            var subtitle = Path.Combine(_root, "embedded.sup");
            await File.WriteAllBytesAsync(subtitle, PgsSubtitleFile.WhiteBox(320, 240, 60, 180, 200, 40, ShowAt, HideAt, startEmpty: true), cancellationToken);
            args.AddRange(["-i", subtitle, "-map", "0:v", "-map", "1:a", "-map", "2:s", "-c:s", "copy"]);
        }
        args.AddRange(["-t", "12", "-c:v", "libx264", "-g", "25", "-c:a", "aac", source]);
        await FfmpegTool.RunAsync(cancellationToken, [.. args]);
        return source;
    }

    private static PlaybackDecisionVM BurnIn(int? subtitleStream, string? externalSubtitle, int startSegment) => new()
    {
        MediaItemId = Guid.NewGuid(),
        Decision = StreamingState.Transcode,
        VideoStrategy = "Transcode",
        AudioStrategy = "Copy",
        SourceAudioCodec = "aac",
        TargetVideoCodec = VideoCodec.H264,
        TargetAudioCodec = AudioCodec.Aac,
        RequiresSubtitleBurnIn = true,
        SelectedVideoStreamIndex = 0,
        SelectedAudioStreamIndex = 1,
        SelectedSubtitleStreamIndex = subtitleStream,
        SelectedSubtitleExternalPath = externalSubtitle,
        SourceDurationSeconds = 12,
        StartPositionSeconds = startSegment * 4,
    };

    private async Task<int> LitPixelsMidSegmentAsync(FFmpegTranscodeService transcoder, Guid mediaItemId, int segment, CancellationToken cancellationToken)
    {
        (await transcoder.EnsureSegmentAvailableAsync(mediaItemId, segment, cancellationToken)).Should().BeTrue();
        return await FfmpegTool.BrightPixelsAsync(Path.Combine(_output, $"{mediaItemId}_{segment}.ts"), 50, cancellationToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_embedded_pgs_subtitle_is_drawn_onto_the_video_while_it_is_on_screen(int startSegment)
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await SourceAsync(withEmbeddedSubtitle: true, cancellationToken);
        var transcoder = Transcoder();
        var decision = BurnIn(subtitleStream: 2, externalSubtitle: null, startSegment);

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, _output, decision, cancellationToken);

            if (startSegment == 0) (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 0, cancellationToken)).Should().Be(0);
            (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 1, cancellationToken)).Should().BeGreaterThan(0);
            (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 2, cancellationToken)).Should().Be(0);
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_external_subtitle_file_stays_on_the_videos_clock(int startSegment)
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await SourceAsync(withEmbeddedSubtitle: false, cancellationToken);
        var external = Path.Combine(_root, "source.en.sup");
        await File.WriteAllBytesAsync(external, PgsSubtitleFile.WhiteBox(320, 240, 60, 180, 200, 40, ShowAt, HideAt, startEmpty: false), cancellationToken);
        var transcoder = Transcoder();
        var decision = BurnIn(subtitleStream: null, external, startSegment);

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, _output, decision, cancellationToken);

            if (startSegment == 0) (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 0, cancellationToken)).Should().Be(0);
            (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 1, cancellationToken)).Should().BeGreaterThan(0);
            (await LitPixelsMidSegmentAsync(transcoder, decision.MediaItemId, 2, cancellationToken)).Should().Be(0);
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }

    [Fact]
    public void A_vobsub_is_read_through_its_index_file()
    {
        var sub = Path.Combine(_root, "Heat.en.sub");
        File.WriteAllBytes(sub, []);
        FFmpegTranscodeService.SubtitleInput(sub).Should().Be(sub);

        File.WriteAllText(Path.ChangeExtension(sub, ".idx"), "# VobSub index file, v7");

        FFmpegTranscodeService.SubtitleInput(sub).Should().Be(Path.ChangeExtension(sub, ".idx"));
        FFmpegTranscodeService.SubtitleInput(null).Should().BeNull();
    }
}
