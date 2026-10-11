using Vora.Application.Settings;
using Vora.Infrastructure.Settings;

namespace Vora.Infrastructure.Tests.Settings;

public class GpuDeviceDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-gpu-" + Guid.NewGuid().ToString("N"));
    private string Dev => Path.Combine(_root, "dev");
    private string DevDri => Path.Combine(_root, "dev", "dri");
    private string SysDrm => Path.Combine(_root, "sys", "class", "drm");

    public GpuDeviceDiscoveryTests()
    {
        Directory.CreateDirectory(DevDri);
        Directory.CreateDirectory(SysDrm);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void RenderNode(string name, string? vendor)
    {
        File.WriteAllText(Path.Combine(DevDri, name), string.Empty);
        if (vendor is null) return;
        var device = Path.Combine(SysDrm, name, "device");
        Directory.CreateDirectory(device);
        File.WriteAllText(Path.Combine(device, "vendor"), vendor + "\n");
    }

    [Fact]
    public void Render_nodes_are_named_after_their_maker()
    {
        RenderNode("renderD129", "0x1002");
        RenderNode("renderD128", "0x8086");
        RenderNode("renderD130", null);
        File.WriteAllText(Path.Combine(DevDri, "card0"), string.Empty);

        var nodes = GpuDeviceDiscovery.FindRenderNodes(DevDri, SysDrm);

        nodes.Select(n => (Path.GetFileName(n.Id), n.Name, n.Vendor)).Should().Equal(
            ("renderD128", "Intel GPU", GpuVendor.Intel),
            ("renderD129", "AMD GPU", GpuVendor.Amd),
            ("renderD130", "Unknown GPU", GpuVendor.Unknown));
    }

    [Fact]
    public void An_nvidia_render_node_is_left_out_because_vaapi_cannot_drive_it()
    {
        RenderNode("renderD128", "0x10de");

        GpuDeviceDiscovery.FindRenderNodes(DevDri, SysDrm).Should().BeEmpty();
    }

    [Fact]
    public void Without_a_dri_folder_there_are_no_render_nodes()
    {
        GpuDeviceDiscovery.FindRenderNodes(Path.Combine(_root, "missing"), SysDrm).Should().BeEmpty();
    }

    [Fact]
    public void Nvidia_gpus_take_their_names_from_nvidia_smi()
    {
        var gpus = GpuDeviceDiscovery.FindNvidiaGpus(Dev, "0, NVIDIA GeForce RTX 3060\n1, NVIDIA RTX A2000\n", hasCudaDriver: true);

        gpus.Should().Equal(
            new GpuDevice("0", "NVIDIA GeForce RTX 3060", GpuVendor.Nvidia),
            new GpuDevice("1", "NVIDIA RTX A2000", GpuVendor.Nvidia));
    }

    [Fact]
    public void Without_nvidia_smi_the_device_nodes_give_the_gpu_numbers()
    {
        File.WriteAllText(Path.Combine(Dev, "nvidia1"), string.Empty);
        File.WriteAllText(Path.Combine(Dev, "nvidia0"), string.Empty);
        File.WriteAllText(Path.Combine(Dev, "nvidiactl"), string.Empty);

        var gpus = GpuDeviceDiscovery.FindNvidiaGpus(Dev, string.Empty, hasCudaDriver: true);

        gpus.Select(g => (g.Id, g.Name)).Should().Equal(("0", "NVIDIA GPU 0"), ("1", "NVIDIA GPU 1"));
    }

    [Fact]
    public void Under_wsl_the_cuda_driver_alone_means_one_nvidia_gpu()
    {
        GpuDeviceDiscovery.FindNvidiaGpus(Dev, string.Empty, hasCudaDriver: true)
            .Should().Equal(new GpuDevice("0", "NVIDIA GPU", GpuVendor.Nvidia));
    }

    [Fact]
    public void No_nvidia_driver_means_no_nvidia_gpu()
    {
        GpuDeviceDiscovery.FindNvidiaGpus(Dev, string.Empty, hasCudaDriver: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData("0x8086", GpuVendor.Intel)]
    [InlineData("0x1002\n", GpuVendor.Amd)]
    [InlineData("0x10DE", GpuVendor.Nvidia)]
    [InlineData("0x1af4", GpuVendor.Unknown)]
    public void Pci_vendor_ids_map_to_gpu_makers(string vendorId, GpuVendor expected)
    {
        GpuDeviceDiscovery.ParseVendor(vendorId).Should().Be(expected);
    }
}
