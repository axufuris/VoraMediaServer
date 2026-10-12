using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Settings;

internal sealed record GpuDevice(string Id, string Name, GpuVendor Vendor)
{
    public bool IsRenderNode => Id.StartsWith("/dev/", StringComparison.Ordinal);
}

internal static partial class GpuDeviceDiscovery
{
    private static readonly string[] CudaDriverPaths =
    [
        "/usr/lib/x86_64-linux-gnu/libcuda.so.1",
        "/usr/lib64/libcuda.so.1",
        "/usr/lib/wsl/lib/libcuda.so.1",
    ];

    public static IReadOnlyList<GpuDevice> Discover() =>
        [.. Safely(() => FindNvidiaGpus("/dev", RunNvidiaSmi(), CudaDriverPaths.Any(File.Exists))), .. Safely(() => FindRenderNodes("/dev/dri", "/sys/class/drm"))];

    private static IReadOnlyList<GpuDevice> Safely(Func<IReadOnlyList<GpuDevice>> find)
    {
        try
        {
            return find();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<GpuDevice> FindRenderNodes(string devDri, string sysClassDrm)
    {
        if (!Directory.Exists(devDri)) return [];

        return Directory.GetFiles(devDri, "renderD*")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Vendor: ReadVendor(Path.Combine(sysClassDrm, Path.GetFileName(path), "device", "vendor"))))
            .Where(node => node.Vendor != GpuVendor.Nvidia)
            .Select(node => new GpuDevice(node.Path, $"{VendorName(node.Vendor)} GPU", node.Vendor))
            .ToList();
    }

    public static IReadOnlyList<GpuDevice> FindNvidiaGpus(string dev, string nvidiaSmiOutput, bool hasCudaDriver)
    {
        var named = ParseNvidiaSmi(nvidiaSmiOutput);
        if (named.Count > 0) return named;

        var indexes = Directory.Exists(dev)
            ? Directory.GetFiles(dev, "nvidia*")
                .Select(Path.GetFileName)
                .Select(name => NvidiaDeviceNode().Match(name ?? string.Empty))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                .Order()
                .ToList()
            : [];
        if (indexes.Count > 0)
        {
            return indexes.Select(i => new GpuDevice(i.ToString(CultureInfo.InvariantCulture), $"NVIDIA GPU {i}", GpuVendor.Nvidia)).ToList();
        }

        return hasCudaDriver ? [new GpuDevice("0", "NVIDIA GPU", GpuVendor.Nvidia)] : [];
    }

    public static IReadOnlyList<GpuDevice> ParseNvidiaSmi(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) && parts[1].Length > 0)
            .Select(parts => new GpuDevice(parts[0], parts[1], GpuVendor.Nvidia))
            .ToList();

    public static GpuVendor ReadVendor(string sysfsVendorFile)
    {
        try
        {
            return File.Exists(sysfsVendorFile) ? ParseVendor(File.ReadAllText(sysfsVendorFile)) : GpuVendor.Unknown;
        }
        catch (IOException)
        {
            return GpuVendor.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return GpuVendor.Unknown;
        }
    }

    public static GpuVendor ParseVendor(string pciVendorId) => pciVendorId.Trim().ToLowerInvariant() switch
    {
        "0x8086" => GpuVendor.Intel,
        "0x1002" => GpuVendor.Amd,
        "0x10de" => GpuVendor.Nvidia,
        _ => GpuVendor.Unknown
    };

    public static string VendorName(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Intel => "Intel",
        GpuVendor.Amd => "AMD",
        GpuVendor.Nvidia => "NVIDIA",
        _ => "Unknown"
    };

    private static string RunNvidiaSmi()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--query-gpu=index,name");
            startInfo.ArgumentList.Add("--format=csv,noheader");

            using var process = Process.Start(startInfo);
            if (process is null) return string.Empty;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                process.Kill(entireProcessTree: true);
                return string.Empty;
            }
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"^nvidia(\d+)$")]
    private static partial Regex NvidiaDeviceNode();
}
