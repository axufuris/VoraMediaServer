using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;
using static Vora.Infrastructure.Tests.Transcoding.TranscodeArgumentScenarios;

namespace Vora.Infrastructure.Tests.Transcoding;

public class TranscodeSummaryTests
{
    private static TranscodeSummary Summarize(Vora.Application.Streaming.ViewModels.PlaybackDecisionVM decision, FFmpegTranscodeService.TranscodeHardware? hardware, FFmpegTranscodeService.TranscodeHost host) =>
        TranscodeSummary.From(FFmpegTranscodeService.BuildFFmpegArguments(Source, Segments, InternalPlaylist, decision, new ServerSetting(), hardware, SegmentLaunch.Uniform(0), host));

    [Fact]
    public void A_cpu_transcode_is_x264_on_the_cpu_and_copies_the_audio()
    {
        var summary = Summarize(Transcode(), hardware: null, JellyfinHost);

        summary.VideoEncoder.Should().Be("libx264");
        summary.EncodeHardware.Should().Be("CPU");
        summary.DecodeHardwareName.Should().BeNull();
        summary.ToneMapping.Should().BeNull();
        summary.AudioEncoder.Should().Be("copy");
        summary.AudioBitrateKbps.Should().BeNull();
    }

    [Fact]
    public void Nvidia_reads_as_nvenc_and_nvdec_with_its_target_and_peak()
    {
        var summary = Summarize(Transcode(VideoCodec.Hevc, outputHeight: 1080, bandwidthKbps: 8000), EncodeOn(Nvidia), JellyfinHost);

        summary.VideoEncoder.Should().Be("hevc_nvenc");
        summary.EncodeHardware.Should().Be("NVIDIA NVENC");
        summary.DecodeHardwareName.Should().Be("NVIDIA NVDEC");
        summary.VideoBitrateKbps.Should().Be(6800);
        summary.VideoPeakKbps.Should().Be(8000);
    }

    [Fact]
    public void Each_tone_mapping_path_names_its_algorithm_and_where_it_runs()
    {
        Summarize(Transcode(hdr: "HDR10", outputHeight: 1080), EncodeOn(Nvidia), JellyfinHost).ToneMapping.Should().Be("hable (GPU, CUDA)");
        Summarize(Transcode(hdr: "HDR10"), EncodeOn(AmdVaapiWithVulkan), PlainHost).ToneMapping.Should().Be("hable (GPU, Vulkan)");
        Summarize(Transcode(hdr: "HDR10", outputHeight: 1080, bandwidthKbps: 8000), EncodeOn(IntelQuickSync), JellyfinHost).ToneMapping.Should().Be("VAAPI (GPU)");
        Summarize(Transcode(hdr: "HDR10"), EncodeOn(Nvidia), PlainHost).ToneMapping.Should().Be("zscale (CPU)");
    }

    [Fact]
    public void Converted_audio_reports_its_codec_channels_and_bitrate()
    {
        var summary = TranscodeSummary.From(["-i", Source, "-c:v", "copy", "-c:a", "aac", "-ac", "2", "-b:a", "192k", "-f", "hls"]);

        summary.VideoEncoder.Should().Be("copy");
        summary.EncodeHardware.Should().BeNull();
        summary.AudioEncoder.Should().Be("aac");
        summary.AudioChannels.Should().Be(2);
        summary.AudioBitrateKbps.Should().Be(192);
    }

    [Fact]
    public void A_progress_line_gives_the_frame_rate_and_speed()
    {
        TranscodeProgress.TryRead("frame= 1234 fps= 96 q=28.0 size=N/A time=00:00:51.20 bitrate=N/A speed=4.01x", out var fps, out var speed).Should().BeTrue();
        fps.Should().Be(96);
        speed.Should().Be(4.01);
    }

    [Fact]
    public void A_progress_line_before_the_speed_is_known_still_counts()
    {
        TranscodeProgress.TryRead("frame=    0 fps=0.0 q=0.0 size=N/A time=N/A bitrate=N/A speed=N/A", out var fps, out var speed).Should().BeTrue();
        fps.Should().Be(0);
        speed.Should().BeNull();
    }

    [Fact]
    public void Other_ffmpeg_output_is_not_progress()
    {
        TranscodeProgress.TryRead("[hls @ 0x55d] Opening '/transcode/heat_3.ts' for writing", out _, out _).Should().BeFalse();
        TranscodeProgress.TryRead("Stream #0:0: Video: hevc (Main 10), yuv420p10le, 3840x1600, 23.98 fps", out _, out _).Should().BeFalse();
    }
}
