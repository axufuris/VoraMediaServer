using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vora.Application.Streaming;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Analysis;

public sealed record DolbyVisionRecord(int? BaseLayerCompatibility);

public sealed record HdrProbeResult(string? HdrType);

public static class HdrClassifier
{
    private const string DolbyVisionRecordType = "DOVI configuration record";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> StandardDynamicRangeTransfers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bt709", "bt470m", "bt470bg", "smpte170m", "smpte240m", "iec61966-2-1", "iec61966-2-4", "bt1361e", "bt2020-10", "bt2020-12",
    };

    public static string? Classify(JsonElement stream)
    {
        var transfer = stream.TryGetProperty("color_transfer", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Classify(transfer, ReadDolbyVision(stream));
    }

    public static string? Classify(string? colorTransfer, DolbyVisionRecord? dolbyVision)
    {
        var baseLayer = colorTransfer?.ToLowerInvariant() switch
        {
            "smpte2084" => "HDR10",
            "arib-std-b67" => "HLG",
            _ => null,
        };

        if (dolbyVision is null) return baseLayer;
        if (baseLayer is not null) return $"DoVi/{baseLayer}";
        return HasStandardDynamicRangeBaseLayer(colorTransfer, dolbyVision) ? null : "DoVi";
    }

    public static bool HasNoKnownBaseLayer(string? hdrType) => DynamicRange.HasNoKnownBaseLayer(hdrType);

    public static async Task<HdrProbeResult?> ProbeAsync(string filePath, int? streamIndex, ILogger logger, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add("-v");
        processInfo.ArgumentList.Add("quiet");
        processInfo.ArgumentList.Add("-print_format");
        processInfo.ArgumentList.Add("json");
        processInfo.ArgumentList.Add("-select_streams");
        processInfo.ArgumentList.Add(streamIndex is { } index ? index.ToString(CultureInfo.InvariantCulture) : "v:0");
        processInfo.ArgumentList.Add("-show_streams");
        processInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitWithTimeoutAsync(ProbeTimeout, logger, cancellationToken);
            return ReadProbe(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the dynamic range of {FilePath}; keeping the scanned value.", filePath);
            return null;
        }
    }

    internal static HdrProbeResult? ReadProbe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) return null;
            return streams.GetArrayLength() == 0 ? null : new HdrProbeResult(Classify(streams[0]));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasStandardDynamicRangeBaseLayer(string? colorTransfer, DolbyVisionRecord dolbyVision) =>
        colorTransfer is not null
        && StandardDynamicRangeTransfers.Contains(colorTransfer)
        && dolbyVision.BaseLayerCompatibility > 0;

    private static DolbyVisionRecord? ReadDolbyVision(JsonElement stream)
    {
        if (!stream.TryGetProperty("side_data_list", out var sideDataList) || sideDataList.ValueKind != JsonValueKind.Array) return null;

        foreach (var sideData in sideDataList.EnumerateArray())
        {
            if (!sideData.TryGetProperty("side_data_type", out var type) || type.GetString() != DolbyVisionRecordType) continue;
            return new DolbyVisionRecord(
                sideData.TryGetProperty("dv_bl_signal_compatibility_id", out var compatibility) && compatibility.TryGetInt32(out var id) ? id : null);
        }

        return null;
    }
}
