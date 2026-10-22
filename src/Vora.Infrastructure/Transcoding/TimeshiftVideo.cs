using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Infrastructure.Analysis;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Transcoding;

public sealed class TimeshiftVideo : ITimeshiftVideo
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly HashSet<string> InterlacedFieldOrders = new(StringComparer.OrdinalIgnoreCase) { "tt", "bb", "tb", "bt" };

    private readonly ISystemSettingsRepository _settings;
    private readonly IHardwareCapabilityService _hardware;
    private readonly ILogger<TimeshiftVideo> _logger;

    public TimeshiftVideo(ISystemSettingsRepository settings, IHardwareCapabilityService hardware, ILogger<TimeshiftVideo> logger)
    {
        _settings = settings;
        _hardware = hardware;
        _logger = logger;
    }

    public async Task<LiveVideoFormat?> ReadFormatAsync(string segmentPath, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in ProbeArguments(segmentPath)) processInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            if (!await process.WaitForExitWithTimeoutAsync(ProbeTimeout, _logger, cancellationToken) || process.ExitCode != 0) return null;
            return ParseFormat(await output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the video format of {Segment}.", segmentPath);
            return null;
        }
    }

    public async Task<TimeshiftVideoArguments> EncodeArgumentsAsync(LiveVideoEncode encode, bool onCpu, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetSettingsAsync();
        if (settings.DisableVideoTranscoding)
        {
            _logger.LogWarning("Video transcoding is turned off, so timeshift copies the channel's video although the player can't play it.");
            return TimeshiftVideoArguments.Copy;
        }

        var encoder = onCpu ? null : await HardwareEncoderChoice.ResolveAsync(settings, _hardware, cancellationToken);
        return TimeshiftVideoArgumentsBuilder.Build(encode, encoder);
    }

    internal static IReadOnlyList<string> ProbeArguments(string segmentPath) =>
    [
        "-v", "error",
        "-select_streams", "v:0",
        "-show_entries", "stream=codec_name,profile,level,height,field_order,avg_frame_rate,r_frame_rate",
        "-of", "json",
        segmentPath,
    ];

    internal static LiveVideoFormat? ParseFormat(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
        {
            return null;
        }

        var stream = streams[0];
        if (Text(stream, "codec_name") is not { } codec) return null;

        var geometry = VideoGeometry.Read(stream);
        var interlaced = Text(stream, "field_order") is { } fieldOrder && InterlacedFieldOrders.Contains(fieldOrder);
        return new LiveVideoFormat(codec, Text(stream, "profile"), Level(stream), geometry.Height, geometry.FrameRate, interlaced);
    }

    private static string? Text(JsonElement stream, string name) =>
        stream.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static int? Level(JsonElement stream) =>
        stream.TryGetProperty("level", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var level) && level > 0
            ? level
            : null;
}
