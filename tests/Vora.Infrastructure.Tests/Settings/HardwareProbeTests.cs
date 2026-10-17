using Vora.Domain.Enums;
using Vora.Infrastructure.Settings;

namespace Vora.Infrastructure.Tests.Settings;

public class HardwareProbeTests
{
    private const string Clip = "-hide_banner -nostdin -loglevel error";
    private const string TestSource = "-f lavfi -i testsrc2=size=1280x720:rate=30 -t 2";

    [Fact]
    public void The_nvidia_test_encodes_on_the_chosen_gpu()
    {
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.Nvidia, "1").Arguments())
            .Should().Be($"{Clip} {TestSource} -c:v h264_nvenc -gpu 1 -f null -");
    }

    [Fact]
    public void The_quick_sync_test_uploads_through_vaapi_like_a_real_transcode()
    {
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.QuickSync, "/dev/dri/renderD128").Arguments())
            .Should().Be($"{Clip} -init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -filter_hw_device va {TestSource} -vf format=nv12,hwupload,hwmap=derive_device=qsv,format=qsv -c:v h264_qsv -f null -");
    }

    [Fact]
    public void The_vaapi_test_uploads_and_encodes_on_the_render_node()
    {
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.Vaapi, "/dev/dri/renderD129").Arguments())
            .Should().Be($"{Clip} -init_hw_device vaapi=va:/dev/dri/renderD129 -filter_hw_device va {TestSource} -vf format=nv12,hwupload -c:v h264_vaapi -f null -");
    }

    [Fact]
    public void The_vulkan_test_runs_the_same_tonemap_chain_as_a_transcode()
    {
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.Vaapi, "/dev/dri/renderD129", VulkanTonemap: true).Arguments())
            .Should().Be($"{Clip} -init_hw_device vaapi=va:/dev/dri/renderD129 -init_hw_device vulkan=vk@va -filter_hw_device va {TestSource} -vf format=p010le,hwupload,hwmap=derive_device=vulkan,format=vulkan,libplacebo=format=nv12:tonemapping=hable:colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv,hwmap=derive_device=vaapi,format=vaapi,scale_vaapi=format=nv12 -c:v h264_vaapi -f null -");
    }

    [Fact]
    public void The_hdr_test_encodes_10_bit_hevc_the_way_a_transcode_that_keeps_hdr_does()
    {
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.Nvidia, "1", Hdr10Bit: true).Arguments())
            .Should().Be($"{Clip} {TestSource} -vf format=p010le -c:v hevc_nvenc -profile:v main10 -gpu 1 -f null -");
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.QuickSync, "/dev/dri/renderD128", Hdr10Bit: true).Arguments())
            .Should().Be($"{Clip} -init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -filter_hw_device va {TestSource} -vf format=p010le,hwupload,hwmap=derive_device=qsv,format=qsv -c:v hevc_qsv -profile:v main10 -f null -");
        string.Join(" ", new HardwareProbe(HardwareAccelerationType.Vaapi, "/dev/dri/renderD129", Hdr10Bit: true).Arguments())
            .Should().Be($"{Clip} -init_hw_device vaapi=va:/dev/dri/renderD129 -filter_hw_device va {TestSource} -vf format=p010le,hwupload -c:v hevc_vaapi -profile:v main10 -f null -");
    }

    [Fact]
    public void A_failure_keeps_the_last_three_lines_ffmpeg_printed()
    {
        ProbeOutcome.LastLines("line one\nNo VA display found for device /dev/dri/renderD128.\nDevice creation failed: -22.\n\nError parsing global options: Invalid argument\n")
            .Should().Be("No VA display found for device /dev/dri/renderD128. Device creation failed: -22. Error parsing global options: Invalid argument");
    }
}
