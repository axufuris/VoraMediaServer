using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Settings;

public sealed class HardwareCapabilityService : IHardwareCapabilityService
{
    private const string RenderNodeHint = " Check that /dev/dri is passed into the container.";

    private readonly ILogger<HardwareCapabilityService> _logger;
    private readonly Lazy<IReadOnlyList<GpuDevice>> _devices;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ProbeOutcome>> _runFfmpeg;
    private readonly ConcurrentDictionary<HardwareProbe, Lazy<Task<ProbeOutcome>>> _probes = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<HardwareAccelerator?>>> _detections = new(StringComparer.Ordinal);

    public HardwareCapabilityService(ILogger<HardwareCapabilityService> logger)
        : this(logger, GpuDeviceDiscovery.Discover, ProbeOutcome.RunFfmpegAsync)
    {
    }

    internal HardwareCapabilityService(
        ILogger<HardwareCapabilityService> logger,
        Func<IReadOnlyList<GpuDevice>> discover,
        Func<IReadOnlyList<string>, CancellationToken, Task<ProbeOutcome>> runFfmpeg)
    {
        _logger = logger;
        _devices = new Lazy<IReadOnlyList<GpuDevice>>(discover);
        _runFfmpeg = runFfmpeg;
    }

    private IReadOnlyList<GpuDevice> Devices => _devices.Value;

    public async Task<HardwareCapabilitiesVM> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var detected = await DetectAsync("Auto", cancellationToken);
        return new HardwareCapabilitiesVM
        {
            Devices = Devices.Select(d => new HardwareDeviceVM { Id = d.Id, Name = d.Name, Vendor = d.Vendor }).ToList(),
            Detected = detected?.Type,
            DetectedDevice = detected?.Device
        };
    }

    public async Task<HardwareAccelerator?> ResolveAsync(HardwareAccelerationType type, string device, CancellationToken cancellationToken = default)
    {
        device = Normalize(device);
        if (type == HardwareAccelerationType.Auto) return await DetectAsync(device, cancellationToken);

        var target = Target(type, device);
        return target is null ? null : await WithVulkanAsync(target, fresh: false).WaitAsync(cancellationToken);
    }

    public async Task<HardwareTestResultVM> TestAsync(HardwareAccelerationType type, string device, CancellationToken cancellationToken = default)
    {
        device = Normalize(device);
        if (type == HardwareAccelerationType.Off)
        {
            return new HardwareTestResultVM { Message = "Hardware acceleration is off, so Vora transcodes on the CPU." };
        }

        if (type == HardwareAccelerationType.Auto)
        {
            var detection = new Lazy<Task<HardwareAccelerator?>>(() => RunDetectionAsync(device, fresh: true));
            _detections[device] = detection;
            var found = await detection.Value.WaitAsync(cancellationToken);
            if (found is null)
            {
                var hint = Devices.Count == 0 ? " No GPU is visible inside the container." : string.Empty;
                return new HardwareTestResultVM { Message = $"Vora didn't find a GPU it can use, so it transcodes on the CPU.{hint}" };
            }
            var picked = await ProbeAsync(new HardwareProbe(found.Type, found.Device), fresh: false);
            return Passed(found, picked, prefix: "Auto picked ");
        }

        var target = Target(type, device);
        if (target is null)
        {
            return new HardwareTestResultVM { Type = type, Message = $"No {VendorFor(type)} GPU is visible inside the container.{RenderNodeHint}" };
        }

        var outcome = await ProbeAsync(new HardwareProbe(target.Type, target.Device), fresh: true).WaitAsync(cancellationToken);
        if (!outcome.Success)
        {
            var hint = target.Type == HardwareAccelerationType.Nvidia ? string.Empty : RenderNodeHint;
            return new HardwareTestResultVM
            {
                Type = target.Type,
                Device = target.Device,
                Message = $"{HardwareArguments.Label(target.Type)} failed: {DetailOrUnknown(outcome)}{hint}"
            };
        }

        var resolved = await WithVulkanAsync(target, fresh: true).WaitAsync(cancellationToken);
        return Passed(resolved, outcome, prefix: string.Empty);
    }

    private static HardwareTestResultVM Passed(HardwareAccelerator hardware, ProbeOutcome outcome, string prefix)
    {
        var seconds = outcome.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        var tonemap = hardware is { Type: HardwareAccelerationType.Vaapi, Vendor: GpuVendor.Amd }
            ? hardware.HasVulkanInterop
                ? " HDR tone mapping runs on the GPU through Vulkan."
                : " HDR tone mapping runs on the CPU because Vulkan isn't available for this GPU."
            : string.Empty;
        return new HardwareTestResultVM
        {
            Success = true,
            Type = hardware.Type,
            Device = hardware.Device,
            Message = $"{prefix}{HardwareArguments.Label(hardware.Type)} on {DeviceText(hardware.Device)}. It encoded a 2-second test clip in {seconds} s.{tonemap}"
        };
    }

    private Task<HardwareAccelerator?> DetectAsync(string device, CancellationToken cancellationToken) =>
        _detections.GetOrAdd(device, d => new Lazy<Task<HardwareAccelerator?>>(() => RunDetectionAsync(d, fresh: false))).Value.WaitAsync(cancellationToken);

    private async Task<HardwareAccelerator?> RunDetectionAsync(string device, bool fresh)
    {
        foreach (var candidate in DetectionCandidates(device))
        {
            var outcome = await ProbeAsync(new HardwareProbe(candidate.Type, candidate.Device), fresh);
            if (outcome.Success) return await WithVulkanAsync(candidate, fresh);
        }
        return null;
    }

    internal IReadOnlyList<HardwareAccelerator> DetectionCandidates(string device)
    {
        if (IsCudaIndex(device)) return [new HardwareAccelerator(HardwareAccelerationType.Nvidia, device, GpuVendor.Nvidia)];
        if (IsRenderNode(device)) return NodeCandidates(Node(device));

        var candidates = new List<HardwareAccelerator>();
        if (Devices.Any(d => d.Vendor == GpuVendor.Nvidia))
        {
            candidates.Add(new HardwareAccelerator(HardwareAccelerationType.Nvidia, null, GpuVendor.Nvidia));
        }
        candidates.AddRange(Devices.Where(d => d.IsRenderNode && d.Vendor == GpuVendor.Intel)
            .Select(node => new HardwareAccelerator(HardwareAccelerationType.QuickSync, node.Id, node.Vendor)));
        candidates.AddRange(Devices.Where(d => d.IsRenderNode)
            .Select(node => new HardwareAccelerator(HardwareAccelerationType.Vaapi, node.Id, node.Vendor)));
        return candidates;
    }

    private static List<HardwareAccelerator> NodeCandidates(GpuDevice node)
    {
        var candidates = new List<HardwareAccelerator>();
        if (node.Vendor == GpuVendor.Intel)
        {
            candidates.Add(new HardwareAccelerator(HardwareAccelerationType.QuickSync, node.Id, node.Vendor));
        }
        candidates.Add(new HardwareAccelerator(HardwareAccelerationType.Vaapi, node.Id, node.Vendor));
        return candidates;
    }

    internal HardwareAccelerator? Target(HardwareAccelerationType type, string device) => type switch
    {
        HardwareAccelerationType.Nvidia => new HardwareAccelerator(HardwareAccelerationType.Nvidia, IsCudaIndex(device) ? device : null, GpuVendor.Nvidia),
        HardwareAccelerationType.QuickSync => PickRenderNode(device, GpuVendor.Intel) is { } intel
            ? new HardwareAccelerator(HardwareAccelerationType.QuickSync, intel.Id, intel.Vendor)
            : null,
        HardwareAccelerationType.Vaapi => PickRenderNode(device, GpuVendor.Intel, GpuVendor.Amd) is { } node
            ? new HardwareAccelerator(HardwareAccelerationType.Vaapi, node.Id, node.Vendor)
            : null,
        _ => null
    };

    private GpuDevice? PickRenderNode(string device, params GpuVendor[] vendors)
    {
        if (IsRenderNode(device)) return Node(device);
        return Devices.FirstOrDefault(d => d.IsRenderNode && vendors.Contains(d.Vendor))
            ?? Devices.FirstOrDefault(d => d.IsRenderNode && d.Vendor == GpuVendor.Unknown);
    }

    private GpuDevice Node(string path) =>
        Devices.FirstOrDefault(d => d.Id == path) ?? new GpuDevice(path, "GPU", GpuVendor.Unknown);

    private async Task<HardwareAccelerator> WithVulkanAsync(HardwareAccelerator hardware, bool fresh)
    {
        if (hardware is not { Type: HardwareAccelerationType.Vaapi, Vendor: GpuVendor.Amd }) return hardware;
        var outcome = await ProbeAsync(new HardwareProbe(hardware.Type, hardware.Device, VulkanTonemap: true), fresh);
        return hardware with { HasVulkanInterop = outcome.Success };
    }

    private Task<ProbeOutcome> ProbeAsync(HardwareProbe probe, bool fresh)
    {
        var run = new Lazy<Task<ProbeOutcome>>(() => RunProbeAsync(probe));
        if (fresh)
        {
            _probes[probe] = run;
            return run.Value;
        }
        return _probes.GetOrAdd(probe, run).Value;
    }

    private async Task<ProbeOutcome> RunProbeAsync(HardwareProbe probe)
    {
        ProbeOutcome outcome;
        try
        {
            outcome = await _runFfmpeg(probe.Arguments(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            outcome = new ProbeOutcome(false, ex.Message, TimeSpan.Zero);
        }
        var what = probe.VulkanTonemap ? "Vulkan tone mapping" : HardwareArguments.Label(probe.Type);
        if (outcome.Success)
        {
            _logger.LogInformation("Hardware test: {Hardware} on {Device} encoded a test clip in {Elapsed:0.0}s.", what, DeviceText(probe.Device), outcome.Elapsed.TotalSeconds);
        }
        else
        {
            _logger.LogInformation("Hardware test: {Hardware} on {Device} is not usable: {Detail}", what, DeviceText(probe.Device), DetailOrUnknown(outcome));
        }
        return outcome;
    }

    private static string DetailOrUnknown(ProbeOutcome outcome) =>
        string.IsNullOrWhiteSpace(outcome.Detail) ? "FFmpeg exited with an error." : outcome.Detail;

    private static string Normalize(string device) => string.IsNullOrWhiteSpace(device) ? "Auto" : device.Trim();

    private static bool IsCudaIndex(string device) => device.Length > 0 && device.All(char.IsAsciiDigit);

    private static bool IsRenderNode(string device) => device.StartsWith("/dev/", StringComparison.Ordinal);

    private static string DeviceText(string? device) => device ?? "the default GPU";

    private static string VendorFor(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.Nvidia => "NVIDIA",
        HardwareAccelerationType.QuickSync => "Intel",
        _ => "Intel or AMD"
    };
}
