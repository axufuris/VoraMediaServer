using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class TimeshiftVideoTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-timeshift-video-" + Guid.NewGuid().ToString("N"));
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IHardwareCapabilityService _hardware = Substitute.For<IHardwareCapabilityService>();

    public TimeshiftVideoTests()
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

    private TimeshiftVideo Video() => new(_settings, _hardware, NullLogger<TimeshiftVideo>.Instance);

    [Fact]
    public void Ffprobes_answer_for_a_transport_stream_segment_becomes_the_channels_format()
    {
        const string json = """
            {
                "programs": [ { "streams": [ { "codec_name": "h264", "profile": "High 10", "level": 51, "height": 1080, "field_order": "progressive", "avg_frame_rate": "25/1" } ] } ],
                "streams": [ { "codec_name": "h264", "profile": "High 10", "height": 1080, "level": 51, "field_order": "progressive", "r_frame_rate": "25/1", "avg_frame_rate": "25/1" } ]
            }
            """;

        TimeshiftVideo.ParseFormat(json).Should().Be(new LiveVideoFormat("h264", "High 10", 51, 1080, 25, false));
    }

    [Theory]
    [InlineData("tt", true)]
    [InlineData("bb", true)]
    [InlineData("progressive", false)]
    [InlineData("unknown", false)]
    public void A_field_order_names_an_interlaced_picture(string fieldOrder, bool interlaced)
    {
        var json = $$"""{ "streams": [ { "codec_name": "h264", "profile": "Main", "level": 40, "field_order": "{{fieldOrder}}" } ] }""";

        TimeshiftVideo.ParseFormat(json)?.Interlaced.Should().Be(interlaced);
    }

    [Fact]
    public void An_unknown_level_and_a_missing_profile_are_left_unknown()
    {
        TimeshiftVideo.ParseFormat("""{ "streams": [ { "codec_name": "mpeg2video", "level": -99 } ] }""")
            .Should().Be(new LiveVideoFormat("mpeg2video", null, null, null, null, false));
    }

    [Theory]
    [InlineData("""{ "streams": [] }""")]
    [InlineData("""{ "programs": [] }""")]
    [InlineData("""{ "streams": [ { "profile": "High" } ] }""")]
    [InlineData("")]
    public void A_segment_without_a_readable_video_stream_has_no_format(string json)
    {
        TimeshiftVideo.ParseFormat(json).Should().BeNull();
    }

    [Fact]
    public async Task Turning_video_transcoding_off_keeps_the_copy()
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { DisableVideoTranscoding = true });

        (await Video().EncodeArgumentsAsync(new LiveVideoEncode(41, null, false), onCpu: false, TestContext.Current.CancellationToken))
            .Should().BeSameAs(TimeshiftVideoArguments.Copy);
    }

    [Fact]
    public async Task The_gpu_is_used_only_when_hardware_encoding_is_on()
    {
        var nvidia = new HardwareAccelerator(HardwareAccelerationType.Nvidia, "0", GpuVendor.Nvidia);
        _hardware.ResolveAsync(HardwareAccelerationType.Auto, "Auto", Arg.Any<CancellationToken>()).Returns(nvidia);
        var encode = new LiveVideoEncode(41, null, false);
        var cancellationToken = TestContext.Current.CancellationToken;

        _settings.GetSettingsAsync().Returns(new ServerSetting { UseHardwareAcceleration = true, UseHardwareEncoding = true, HardwareAccelerationType = HardwareAccelerationType.Auto, HardwareTranscodingDevice = "Auto" });
        (await Video().EncodeArgumentsAsync(encode, onCpu: false, cancellationToken)).Encoder.Should().Be("NVIDIA (NVENC)");
        (await Video().EncodeArgumentsAsync(encode, onCpu: true, cancellationToken)).Encoder.Should().Be("libx264");

        _settings.GetSettingsAsync().Returns(new ServerSetting { UseHardwareAcceleration = true, UseHardwareEncoding = false });
        (await Video().EncodeArgumentsAsync(encode, onCpu: false, cancellationToken)).Encoder.Should().Be("libx264");
    }

    [Fact]
    public async Task A_high_10_stream_at_level_5_1_is_read_from_its_segment()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var segment = await High10Async(seconds: 4, cancellationToken);

        (await Video().ReadFormatAsync(segment, cancellationToken)).Should().Be(new LiveVideoFormat("h264", "High 10", 51, 1080, 25, false));
    }

    [Fact]
    public async Task An_interlaced_stream_is_read_as_interlaced()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var segment = Path.Combine(_folder, "interlaced.ts");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=720x576:rate=25:duration=2",
            "-c:v", "libx264", "-preset", "veryfast", "-flags", "+ildct+ilme", "-x264-params", "tff=1", "-pix_fmt", "yuv420p", segment);

        (await Video().ReadFormatAsync(segment, cancellationToken))?.Interlaced.Should().BeTrue();
    }

    [Fact]
    public async Task A_high_10_stream_at_level_5_1_comes_out_as_8_bit_high_at_level_4_1_in_four_second_segments()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = await High10Async(seconds: 8, cancellationToken);
        var encode = LiveVideoDecision.EncodeFor(new LiveVideoFormat("h264", "High 10", 51, 1080, 25, false), new LiveVideoLimits(["h264"], 42, ["baseline", "main", "high"]));
        var video = TimeshiftVideoArgumentsBuilder.Build(encode, encoder: null);
        var playlist = Path.Combine(_folder, "index.m3u8");

        await FfmpegTool.RunAsync(cancellationToken,
        [
            "-i", source,
            .. video.Video,
            "-c:a", "aac", "-b:a", "128k",
            "-f", "hls", "-hls_time", "4", "-hls_list_size", "0",
            "-hls_segment_filename", Path.Combine(_folder, "seg_%03d.ts"),
            playlist,
        ]);

        (await Video().ReadFormatAsync(Path.Combine(_folder, "seg_000.ts"), cancellationToken)).Should().Be(new LiveVideoFormat("h264", "High", 41, 1080, 25, false));
        (await File.ReadAllLinesAsync(playlist, cancellationToken)).Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal)).Take(2)
            .Should().Equal("#EXTINF:4.000000,", "#EXTINF:4.000000,");
    }

    private async Task<string> High10Async(int seconds, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_folder, $"high10-{seconds}.ts");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", $"testsrc2=size=1920x1080:rate=25:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=duration={seconds}",
            "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "high10", "-level", "5.1", "-pix_fmt", "yuv420p10le",
            "-c:a", "aac", path);
        return path;
    }
}
