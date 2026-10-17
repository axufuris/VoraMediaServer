using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Settings;

namespace Vora.Infrastructure.Tests.Settings;

public class HardwareCapabilityServiceTests
{
    private const string IntelNode = "/dev/dri/renderD128";
    private const string AmdNode = "/dev/dri/renderD129";

    private static readonly GpuDevice NvidiaGpu = new("0", "NVIDIA GeForce RTX 3060", GpuVendor.Nvidia);
    private static readonly GpuDevice IntelGpu = new(IntelNode, "Intel GPU", GpuVendor.Intel);
    private static readonly GpuDevice AmdGpu = new(AmdNode, "AMD GPU", GpuVendor.Amd);

    private readonly List<string> _runs = new();
    private Func<string, bool> _works = _ => true;

    private HardwareCapabilityService Service(params GpuDevice[] devices) =>
        new(NullLogger<HardwareCapabilityService>.Instance, () => devices, (args, _) =>
        {
            var command = string.Join(" ", args);
            _runs.Add(command);
            return Task.FromResult(new ProbeOutcome(_works(command), _works(command) ? string.Empty : "Device creation failed: -22.", TimeSpan.FromSeconds(0.5)));
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Auto_never_tries_nvidia_on_a_server_without_an_nvidia_gpu()
    {
        var resolved = await Service(IntelGpu).ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.QuickSync, IntelNode, GpuVendor.Intel, EncodesHdr: true));
        _runs.Should().NotContain(run => run.Contains("nvenc"));
    }

    [Fact]
    public async Task Auto_prefers_nvidia_when_one_is_present()
    {
        var resolved = await Service(NvidiaGpu, IntelGpu).ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia, EncodesHdr: true));
    }

    [Fact]
    public async Task Auto_moves_on_to_vaapi_when_quick_sync_fails()
    {
        _works = command => !command.Contains("h264_qsv");

        var resolved = await Service(IntelGpu).ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Vaapi, IntelNode, GpuVendor.Intel, EncodesHdr: true));
    }

    [Fact]
    public async Task Auto_falls_back_to_the_cpu_when_no_gpu_passes_the_test()
    {
        _works = _ => false;

        var resolved = await Service(NvidiaGpu, IntelGpu, AmdGpu).ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        resolved.Should().BeNull();
        _runs.Should().HaveCount(4);
    }

    [Fact]
    public async Task Auto_with_a_pinned_render_node_only_tries_that_gpu()
    {
        var resolved = await Service(IntelGpu, AmdGpu).ResolveAsync(HardwareAccelerationType.Auto, AmdNode, Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Vaapi, AmdNode, GpuVendor.Amd, HasVulkanInterop: true, EncodesHdr: true));
        _runs.Should().OnlyContain(run => run.Contains(AmdNode));
    }

    [Fact]
    public async Task Off_uses_the_cpu_without_testing_anything()
    {
        var resolved = await Service(NvidiaGpu, IntelGpu).ResolveAsync(HardwareAccelerationType.Off, "Auto", Ct);

        resolved.Should().BeNull();
        _runs.Should().BeEmpty();
    }

    [Fact]
    public async Task Nvidia_ignores_a_render_node_that_cuda_cannot_open()
    {
        var resolved = await Service(NvidiaGpu, IntelGpu).ResolveAsync(HardwareAccelerationType.Nvidia, IntelNode, Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia, EncodesHdr: true));
    }

    [Fact]
    public async Task Quick_sync_uses_the_intel_gpu_when_the_device_is_auto()
    {
        var resolved = await Service(AmdGpu, IntelGpu).ResolveAsync(HardwareAccelerationType.QuickSync, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.QuickSync, IntelNode, GpuVendor.Intel, EncodesHdr: true));
    }

    [Fact]
    public async Task Quick_sync_without_an_intel_gpu_falls_back_to_the_cpu()
    {
        var resolved = await Service(AmdGpu).ResolveAsync(HardwareAccelerationType.QuickSync, "Auto", Ct);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task Amd_vaapi_tonemaps_on_the_cpu_when_vulkan_is_unavailable()
    {
        _works = command => !command.Contains("libplacebo");

        var resolved = await Service(AmdGpu).ResolveAsync(HardwareAccelerationType.Vaapi, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Vaapi, AmdNode, GpuVendor.Amd, HasVulkanInterop: false, EncodesHdr: true));
        resolved?.TonemapsOnGpu.Should().BeFalse();
    }

    [Fact]
    public async Task Detection_runs_once_and_a_test_runs_again()
    {
        var service = Service(IntelGpu);
        await service.ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);
        await service.ResolveAsync(HardwareAccelerationType.Auto, "Auto", Ct);
        _runs.Should().HaveCount(2);

        await service.TestAsync(HardwareAccelerationType.QuickSync, "Auto", Ct);

        _runs.Should().HaveCount(4);
    }

    [Fact]
    public async Task A_passing_test_says_what_encoded_the_clip()
    {
        var result = await Service(IntelGpu).TestAsync(HardwareAccelerationType.QuickSync, "Auto", Ct);

        result.Success.Should().BeTrue();
        result.Type.Should().Be(HardwareAccelerationType.QuickSync);
        result.Device.Should().Be(IntelNode);
        result.Message.Should().Be("Intel Quick Sync on /dev/dri/renderD128. It encoded a 2-second test clip in 0.5 s. It encodes 10-bit HEVC, so a transcode can keep HDR for a player that shows it.");
    }

    [Fact]
    public async Task A_gpu_that_cannot_encode_10_bit_hevc_still_transcodes_but_tone_maps_hdr()
    {
        _works = command => !command.Contains("main10");

        var service = Service(NvidiaGpu);
        var resolved = await service.ResolveAsync(HardwareAccelerationType.Nvidia, "Auto", Ct);
        var result = await service.TestAsync(HardwareAccelerationType.Nvidia, "Auto", Ct);

        resolved.Should().Be(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia, EncodesHdr: false));
        result.Success.Should().BeTrue();
        result.Message.Should().EndWith("It didn't encode 10-bit HEVC, so a transcode tone-maps HDR to SDR.");
    }

    [Fact]
    public async Task A_failing_test_shows_the_ffmpeg_error_and_how_to_pass_the_gpu_in()
    {
        _works = _ => false;

        var result = await Service(IntelGpu).TestAsync(HardwareAccelerationType.Vaapi, "Auto", Ct);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("VAAPI failed: Device creation failed: -22. Check that /dev/dri is passed into the container.");
    }

    [Fact]
    public async Task Testing_auto_reports_what_it_picked()
    {
        _works = command => !command.Contains("h264_qsv");

        var result = await Service(IntelGpu).TestAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        result.Success.Should().BeTrue();
        result.Message.Should().StartWith("Auto picked VAAPI on /dev/dri/renderD128.");
    }

    [Fact]
    public async Task Testing_auto_without_any_gpu_says_the_cpu_is_used()
    {
        var result = await Service().TestAsync(HardwareAccelerationType.Auto, "Auto", Ct);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Vora didn't find a GPU it can use, so it transcodes on the CPU. No GPU is visible inside the container.");
    }

    [Fact]
    public async Task Capabilities_list_every_gpu_and_what_auto_found()
    {
        var capabilities = await Service(NvidiaGpu, IntelGpu).GetCapabilitiesAsync(Ct);

        capabilities.Devices.Select(d => (d.Id, d.Name, d.Vendor)).Should().Equal(
            ("0", "NVIDIA GeForce RTX 3060", GpuVendor.Nvidia),
            (IntelNode, "Intel GPU", GpuVendor.Intel));
        capabilities.Detected.Should().Be(HardwareAccelerationType.Nvidia);
        capabilities.DetectedDevice.Should().BeNull();
    }
}
