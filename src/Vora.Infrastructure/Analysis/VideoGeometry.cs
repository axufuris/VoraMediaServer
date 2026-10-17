using System.Globalization;
using System.Text.Json;

namespace Vora.Infrastructure.Analysis;

public sealed record VideoGeometryReading(int? Width, int? Height, double? FrameRate);

public static class VideoGeometry
{
    private const double MaxFrameRate = 1000;

    public static VideoGeometryReading Read(JsonElement stream) => new(
        Dimension(stream, "width"),
        Dimension(stream, "height"),
        FrameRate(stream, "avg_frame_rate") ?? FrameRate(stream, "r_frame_rate"));

    private static int? Dimension(JsonElement stream, string name) =>
        stream.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var pixels) && pixels > 0
            ? pixels
            : null;

    internal static double? FrameRate(JsonElement stream, string name)
    {
        if (!stream.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = 1.0;
        if (parts.Length == 2 && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator)) return null;
        if (denominator <= 0) return null;

        var rate = numerator / denominator;
        return rate is > 0 and <= MaxFrameRate ? Math.Round(rate, 3) : null;
    }
}
