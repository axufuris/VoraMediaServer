using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public class TimeshiftVideoArgumentsTests
{
    private const string RenderNode = "/dev/dri/renderD128";
    private const string Keyframes = "expr:gte(t,n_forced*4)";

    private static readonly LiveVideoEncode Level41 = new(41, null, false);

    [Fact]
    public void The_cpu_encodes_h264_high_at_the_planned_level_with_a_keyframe_every_segment()
    {
        var arguments = TimeshiftVideoArgumentsBuilder.Build(Level41, encoder: null);

        arguments.BeforeInput.Should().BeEmpty();
        arguments.Video.Should().Equal(
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
            "-profile:v", "high", "-level", "41",
            "-pix_fmt", "yuv420p",
            "-force_key_frames", Keyframes);
        arguments.Should().BeEquivalentTo(new { Encoder = "libx264", OnGpu = false, Encodes = true });
    }

    [Fact]
    public void Nvenc_takes_the_chosen_gpu_and_makes_each_forced_keyframe_an_idr()
    {
        var arguments = TimeshiftVideoArgumentsBuilder.Build(Level41, new HardwareAccelerator(HardwareAccelerationType.Nvidia, "1", GpuVendor.Nvidia));

        arguments.BeforeInput.Should().BeEmpty();
        arguments.Video.Should().Equal(
            "-c:v", "h264_nvenc", "-gpu", "1", "-preset", "p4", "-cq", "28", "-forced-idr", "1",
            "-profile:v", "high", "-level", "41",
            "-pix_fmt", "yuv420p",
            "-force_key_frames", Keyframes);
        arguments.Should().BeEquivalentTo(new { Encoder = "NVIDIA (NVENC)", OnGpu = true });
    }

    [Fact]
    public void Nvenc_without_a_chosen_gpu_leaves_the_choice_to_the_driver()
    {
        TimeshiftVideoArgumentsBuilder.Build(Level41, new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia))
            .Video.Should().NotContain("-gpu");
    }

    [Fact]
    public void Quick_sync_opens_its_device_before_the_input_and_takes_nv12()
    {
        var arguments = TimeshiftVideoArgumentsBuilder.Build(Level41, new HardwareAccelerator(HardwareAccelerationType.QuickSync, RenderNode, GpuVendor.Intel));

        arguments.BeforeInput.Should().Equal("-init_hw_device", $"vaapi=va:{RenderNode}", "-init_hw_device", "qsv=qs@va", "-filter_hw_device", "va");
        arguments.Video.Should().Equal(
            "-vf", "format=nv12",
            "-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", "28", "-forced_idr", "1",
            "-profile:v", "high", "-level", "41",
            "-force_key_frames", Keyframes);
        arguments.Encoder.Should().Be("Intel Quick Sync");
    }

    [Fact]
    public void Vaapi_uploads_the_picture_to_the_gpu_before_encoding()
    {
        var arguments = TimeshiftVideoArgumentsBuilder.Build(Level41, new HardwareAccelerator(HardwareAccelerationType.Vaapi, RenderNode, GpuVendor.Amd));

        arguments.BeforeInput.Should().Equal("-init_hw_device", $"vaapi=va:{RenderNode}", "-filter_hw_device", "va");
        arguments.Video.Should().Equal(
            "-vf", "format=nv12,hwupload",
            "-c:v", "h264_vaapi", "-qp", "28",
            "-profile:v", "high", "-level", "41",
            "-force_key_frames", Keyframes);
    }

    [Fact]
    public void Deinterlacing_and_scaling_happen_on_the_cpu_before_the_upload()
    {
        var arguments = TimeshiftVideoArgumentsBuilder.Build(new LiveVideoEncode(42, 1080, Deinterlace: true), new HardwareAccelerator(HardwareAccelerationType.Vaapi, RenderNode, GpuVendor.Amd));

        arguments.Video.Take(2).Should().Equal("-vf", "yadif,scale=-2:'min(ih,1080)',format=nv12,hwupload");
        arguments.Video.Should().ContainInOrder("-level", "42");
    }

    [Fact]
    public void An_accelerator_that_isnt_a_gpu_encodes_on_the_cpu()
    {
        TimeshiftVideoArgumentsBuilder.Build(Level41, new HardwareAccelerator(HardwareAccelerationType.Off, null, GpuVendor.Unknown))
            .Should().BeEquivalentTo(new { Encoder = "libx264", OnGpu = false });
    }
}
