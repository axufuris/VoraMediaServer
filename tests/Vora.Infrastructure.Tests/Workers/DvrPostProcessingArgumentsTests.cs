using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public class DvrPostProcessingArgumentsTests
{
    private const string Input = "/app/data/iptv/dvr/News at 6 \"Live\".ts";
    private const string Output = "/app/data/iptv/dvr/News at 6 \"Live\".mp4";
    private const string AudioAndOutput = "-c:a aac -b:a 128k -movflags +faststart -y";

    private static ServerSetting Settings(int x264Preset = 2, int hevc = 0) => new() { BackgroundX264Preset = x264Preset, EnableHevcEncoding = hevc };

    private static List<string> Expected(params string[] middle) => ["-i", Input, .. middle, .. AudioAndOutput.Split(' '), Output];

    [Fact]
    public void Without_a_gpu_the_recording_encodes_with_x264()
    {
        DvrPostProcessingWorker.BuildFfmpegArgs(Input, Output, Settings(), encoder: null)
            .Should().Equal(Expected("-c:v", "libx264", "-preset", "veryfast", "-crf", "23"));
    }

    [Fact]
    public void Nvidia_encodes_on_the_chosen_gpu()
    {
        var nvidia = new HardwareAccelerator(HardwareAccelerationType.Nvidia, "1", GpuVendor.Nvidia);

        DvrPostProcessingWorker.BuildFfmpegArgs(Input, Output, Settings(hevc: 1), nvidia)
            .Should().Equal(Expected("-c:v", "hevc_nvenc", "-gpu", "1", "-preset", "p4", "-cq", "28"));
    }

    [Fact]
    public void Quick_sync_opens_the_render_node_and_uses_a_preset_it_supports()
    {
        var quickSync = new HardwareAccelerator(HardwareAccelerationType.QuickSync, "/dev/dri/renderD128", GpuVendor.Intel);

        DvrPostProcessingWorker.BuildFfmpegArgs(Input, Output, Settings(x264Preset: 0), quickSync)
            .Should().Equal([
                "-init_hw_device", "vaapi=va:/dev/dri/renderD128", "-init_hw_device", "qsv=qs@va", "-filter_hw_device", "va",
                .. Expected("-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", "28")]);
    }

    [Fact]
    public void Vaapi_uploads_the_decoded_frames_before_encoding()
    {
        var vaapi = new HardwareAccelerator(HardwareAccelerationType.Vaapi, "/dev/dri/renderD129", GpuVendor.Amd);

        DvrPostProcessingWorker.BuildFfmpegArgs(Input, Output, Settings(), vaapi)
            .Should().Equal([
                "-init_hw_device", "vaapi=va:/dev/dri/renderD129", "-filter_hw_device", "va",
                .. Expected("-vf", "format=nv12,hwupload", "-c:v", "h264_vaapi", "-qp", "28")]);
    }

    [Fact]
    public void With_transcoding_disabled_the_recording_is_only_remuxed()
    {
        var settings = Settings();
        settings.DisableVideoTranscoding = true;

        DvrPostProcessingWorker.BuildFfmpegArgs(Input, Output, settings, encoder: null)
            .Should().Equal("-i", Input, "-c", "copy", "-movflags", "+faststart", "-y", Output);
    }
}
