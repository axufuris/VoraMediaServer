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

public sealed class GpuFailureRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-gpu-retry-" + Guid.NewGuid().ToString("N"));

    public GpuFailureRetryTests()
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
    public async Task A_gpu_transcode_that_dies_restarts_on_the_cpu_instead_of_hanging_the_stream()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = Path.Combine(_root, "source.mkv");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "12", "-c:v", "libx264", "-c:a", "aac", source);
        var output = Path.Combine(_root, "transcode");
        Directory.CreateDirectory(output);

        var settings = new ServerSetting { TranscoderTempDirectory = output, UseHardwareAcceleration = true, UseHardwareEncoding = true, MaxGpuTranscodes = 0 };
        var repository = Substitute.For<ISystemSettingsRepository>();
        repository.GetSettingsAsync().Returns(settings);
        var hardware = Substitute.For<IHardwareCapabilityService>();
        hardware.ResolveAsync(Arg.Any<HardwareAccelerationType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HardwareAccelerator(HardwareAccelerationType.QuickSync, "/dev/dri/renderD999", GpuVendor.Intel));
        var services = new ServiceCollection().AddScoped(_ => repository).BuildServiceProvider();
        var transcoder = new FFmpegTranscodeService(services, hardware, NullLogger<FFmpegTranscodeService>.Instance);

        var decision = new PlaybackDecisionVM
        {
            MediaItemId = Guid.NewGuid(),
            Decision = StreamingState.Transcode,
            VideoStrategy = "Transcode",
            AudioStrategy = "Copy",
            SourceAudioCodec = "aac",
            TargetVideoCodec = VideoCodec.H264,
            TargetAudioCodec = AudioCodec.Aac,
            SourceDurationSeconds = 12,
        };

        try
        {
            await transcoder.StartTranscodeSessionAsync(source, output, decision, cancellationToken);

            File.Exists(Path.Combine(output, $"{decision.MediaItemId}_0.ts")).Should().BeTrue();
            (await transcoder.EnsureSegmentAvailableAsync(decision.MediaItemId, 1, cancellationToken)).Should().Be(SegmentAvailability.Ready);
        }
        finally
        {
            await transcoder.StopTranscodeSessionAsync(decision.MediaItemId);
        }
    }
}
