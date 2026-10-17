using System.Globalization;
using System.Text.RegularExpressions;

namespace Vora.Infrastructure.Transcoding;

internal sealed partial record TranscodeSummary(
    string? VideoEncoder,
    int? VideoBitrateKbps,
    int? VideoPeakKbps,
    string? HardwareDecode,
    string? ToneMapping,
    string? AudioEncoder,
    int? AudioChannels,
    int? AudioBitrateKbps)
{
    public string? EncodeHardware => VideoEncoder switch
    {
        null or "copy" => null,
        _ when VideoEncoder.EndsWith("_nvenc", StringComparison.Ordinal) => "NVIDIA NVENC",
        _ when VideoEncoder.EndsWith("_qsv", StringComparison.Ordinal) => "Intel Quick Sync",
        _ when VideoEncoder.EndsWith("_vaapi", StringComparison.Ordinal) => "VAAPI",
        _ when VideoEncoder.EndsWith("_amf", StringComparison.Ordinal) => "AMD AMF",
        _ => "CPU"
    };

    public string? DecodeHardwareName => HardwareDecode switch
    {
        null => null,
        "cuda" => "NVIDIA NVDEC",
        "qsv" => "Intel Quick Sync",
        "vaapi" => "VAAPI",
        _ => HardwareDecode
    };

    public static TranscodeSummary From(IReadOnlyList<string> arguments)
    {
        string? ValueOf(string flag)
        {
            for (var i = 0; i < arguments.Count - 1; i++)
            {
                if (arguments[i] == flag) return arguments[i + 1];
            }
            return null;
        }

        var filters = string.Join(' ', arguments.Where((_, i) => i > 0 && arguments[i - 1] is "-vf" or "-filter_complex"));
        var audioEncoder = ValueOf("-c:a");

        return new TranscodeSummary(
            ValueOf("-c:v"),
            Kilobits(ValueOf("-b:v")),
            Kilobits(ValueOf("-maxrate")),
            ValueOf("-hwaccel"),
            ToneMappingIn(filters),
            audioEncoder,
            audioEncoder == "copy" ? null : int.TryParse(ValueOf("-ac"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels) ? channels : null,
            audioEncoder == "copy" ? null : Kilobits(ValueOf("-b:a")));
    }

    private static int? Kilobits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var factor = 1.0;
        if (text.EndsWith('k') || text.EndsWith('K'))
        {
            text = text[..^1];
        }
        else if (text.EndsWith('M'))
        {
            text = text[..^1];
            factor = 1000;
        }
        else
        {
            factor = 0.001;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0
            ? (int)Math.Round(number * factor)
            : null;
    }

    private static string? ToneMappingIn(string filters)
    {
        if (string.IsNullOrEmpty(filters)) return null;

        var placebo = Placebo().Match(filters);
        if (placebo.Success) return $"{placebo.Groups["algorithm"].Value} (GPU, Vulkan)";

        var cuda = Cuda().Match(filters);
        if (cuda.Success) return $"{cuda.Groups["algorithm"].Value} (GPU, CUDA)";

        if (filters.Contains("tonemap_vaapi", StringComparison.Ordinal)) return "VAAPI (GPU)";

        var opencl = OpenCl().Match(filters);
        if (opencl.Success) return $"{opencl.Groups["algorithm"].Value} (GPU, OpenCL)";

        var cpu = Cpu().Match(filters);
        if (cpu.Success) return $"{cpu.Groups["algorithm"].Value} (CPU)";

        return ZscaleToSdr().IsMatch(filters) ? "zscale (CPU)" : null;
    }

    [GeneratedRegex(@"zscale=[^,;\[]*\bt=bt709")]
    private static partial Regex ZscaleToSdr();

    [GeneratedRegex(@"libplacebo=[^,;\[]*?tonemapping=(?<algorithm>[\w.\-]+)")]
    private static partial Regex Placebo();

    [GeneratedRegex(@"tonemap_cuda=tonemap=(?<algorithm>[\w\-]+)")]
    private static partial Regex Cuda();

    [GeneratedRegex(@"tonemap_opencl=tonemap=(?<algorithm>[\w\-]+)")]
    private static partial Regex OpenCl();

    [GeneratedRegex(@"(?<![_\w])tonemap=tonemap=(?<algorithm>[\w\-]+)")]
    private static partial Regex Cpu();
}

internal sealed record TranscodeProgressReading(double? Fps, double? Speed);

internal static partial class TranscodeProgress
{
    public static bool TryRead(string line, out double? fps, out double? speed)
    {
        fps = null;
        speed = null;
        if (!line.StartsWith("frame=", StringComparison.Ordinal) && !line.Contains(" fps=", StringComparison.Ordinal)) return false;

        var match = Progress().Match(line);
        if (!match.Success) return false;

        fps = Number(match.Groups["fps"].Value);
        speed = Number(match.Groups["speed"].Value);
        return true;
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    [GeneratedRegex(@"fps=\s*(?<fps>[\d.]+).*?speed=\s*(?<speed>[\d.]+|N/A)x?")]
    private static partial Regex Progress();
}
