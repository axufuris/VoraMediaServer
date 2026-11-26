using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Transcoding;

/// <summary>
/// ffprobe of a segment the transcoder just wrote, summarised for the log: the codec, its tag, profile,
/// level, pixel format and size, the audio, and how far the audio starts from the video. That is what a
/// player is handed, so "plays but shows no picture" (a codec or tag the player cannot render) and
/// "audio drops out at segment edges" (an audio start that drifts from the video's) can be read from the
/// server log. Best effort: any failure returns nothing and never touches playback.
/// </summary>
internal static class SegmentProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    public static async Task<string?> ProbeAsync(string segmentPath, ILogger logger, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-v", "error", "-of", "json",
            "-show_entries", "stream=codec_type,codec_name,codec_tag_string,profile,level,pix_fmt,width,height,coded_width,coded_height,channels,sample_rate,start_time",
            segmentPath
        })
        {
            processInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            if (!await process.WaitForExitWithTimeoutAsync(ProbeTimeout, logger, cancellationToken)) return null;
            return Describe(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not probe the segment {Segment}.", Path.GetFileName(segmentPath));
            return null;
        }
    }

    /// <summary>Reads ffprobe's JSON into "video hevc ...; audio eac3 ...; audio starts 0.020s after the video". Null when it has no streams.</summary>
    internal static string? Describe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) return null;

            var parts = new List<string>();
            double? videoStart = null;
            double? audioStart = null;
            foreach (var stream in streams.EnumerateArray())
            {
                var type = Str(stream, "codec_type");
                var start = Number(stream, "start_time");
                if (type == "video")
                {
                    videoStart ??= start;
                    parts.Add(Video(stream, start));
                }
                else if (type == "audio")
                {
                    audioStart ??= start;
                    parts.Add(Audio(stream, start));
                }
            }

            if (parts.Count == 0) return null;
            if (videoStart is { } v && audioStart is { } a) parts.Add($"audio starts {Seconds(a - v, signed: true)} from the video");
            return string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Video(JsonElement stream, double? start)
    {
        var fields = new List<string> { "video", Str(stream, "codec_name") ?? "?" };
        if (Str(stream, "codec_tag_string") is { } tag && tag != "[0][0][0][0]") fields.Add($"tag {tag}");
        if (Str(stream, "profile") is { } profile) fields.Add(profile);
        if (Whole(stream, "level") is { } level) fields.Add($"level {level}");
        if (Str(stream, "pix_fmt") is { } pixelFormat) fields.Add(pixelFormat);
        var width = Whole(stream, "width");
        var height = Whole(stream, "height");
        if (width is not null && height is not null) fields.Add($"{width}x{height}");
        var codedWidth = Whole(stream, "coded_width");
        var codedHeight = Whole(stream, "coded_height");
        if (codedWidth is not null && codedHeight is not null && (codedWidth != width || codedHeight != height))
        {
            fields.Add($"coded {codedWidth}x{codedHeight}");
        }
        if (start is { } s) fields.Add($"start {Seconds(s)}");
        return string.Join(' ', fields);
    }

    private static string Audio(JsonElement stream, double? start)
    {
        var fields = new List<string> { "audio", Str(stream, "codec_name") ?? "?" };
        if (Str(stream, "profile") is { } profile) fields.Add(profile);
        if (Whole(stream, "channels") is { } channels) fields.Add($"{channels}ch");
        if (Whole(stream, "sample_rate") is { } rate) fields.Add($"{rate}Hz");
        if (start is { } s) fields.Add($"start {Seconds(s)}");
        return string.Join(' ', fields);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    // ffprobe prints most numbers as JSON numbers and a few (sample_rate, start_time) as strings.
    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static long? Whole(JsonElement element, string name) => Number(element, name) is { } value && double.IsFinite(value) ? (long)value : null;

    private static string Seconds(double value, bool signed = false) =>
        (signed && value >= 0 ? "+" : "") + value.ToString("0.000", CultureInfo.InvariantCulture) + "s";
}
