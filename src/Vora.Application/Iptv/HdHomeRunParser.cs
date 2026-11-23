using System.Globalization;
using System.Text.Json;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record HdHomeRunDevice(string Name, string? Model, string? DeviceId, int? TunerCount, string? FirmwareVersion = null);

public sealed record HdHomeRunTuner(
    int Index,
    bool InUse,
    string? ChannelNumber,
    string? ChannelName,
    long? FrequencyHz,
    int? SignalStrengthPercent,
    int? SignalQualityPercent,
    int? SymbolQualityPercent,
    long? BitsPerSecond,
    string? TargetAddress);

/// <summary>
/// Reads the two documents every HDHomeRun serves over HTTP: <c>/discover.json</c> (the device) and
/// <c>/lineup.json</c> (its channels). Only fields the vendor documents are required, and every one is
/// read leniently, because models and firmware differ.
/// </summary>
public static class HdHomeRunParser
{
    public const int DefaultStreamPort = 5004;
    public const string DiscoverPath = "discover.json";
    public const string LineupPath = "lineup.json";
    public const string StatusPath = "status.json";
    public const string GuideAddress = "https://api.hdhomerun.com/api/xmltv";

    private const int MaxNameLength = 256;
    private const int MaxIdLength = 256;
    private const string DrmTag = "drm";
    private const string TunerResourcePrefix = "tuner";

    public static HdHomeRunDevice ParseDevice(string json)
    {
        var root = Root(json);
        var model = Text(root, "ModelNumber");
        var name = Text(root, "FriendlyName") ?? model ?? "HDHomeRun";
        return new HdHomeRunDevice(Limit(name, MaxNameLength), model, Text(root, "DeviceID"), Number(root, "TunerCount") is > 0 and var tuners ? tuners : null, Text(root, "FirmwareVersion"));
    }

    public static string? DeviceAuth(string json) => Text(Root(json), "DeviceAuth");

    public static string GuideUrl(string deviceAuth) => $"{GuideAddress}?DeviceAuth={Uri.EscapeDataString(deviceAuth)}";

    public static List<HdHomeRunTuner> ParseStatus(string json)
    {
        var root = Root(json);
        if (root.ValueKind != JsonValueKind.Array) throw new IptvSourceException("The device's tuner status isn't in the format an HDHomeRun uses.");

        var tuners = new List<HdHomeRunTuner>();
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || TunerIndex(Text(entry, "Resource")) is not { } index) continue;

            var number = Text(entry, "VctNumber");
            var name = Text(entry, "VctName");
            var frequency = Long(entry, "Frequency");
            var target = Text(entry, "TargetIP");
            tuners.Add(new HdHomeRunTuner(
                index,
                number is not null || name is not null || target is not null || frequency is > 0,
                number is null ? null : Limit(number, MaxIdLength),
                name is null ? null : Limit(name, MaxNameLength),
                frequency is > 0 ? frequency : null,
                Percent(entry, "SignalStrengthPercent"),
                Percent(entry, "SignalQualityPercent"),
                Percent(entry, "SymbolQualityPercent"),
                Long(entry, "NetworkRate") is > 0 and var rate ? rate : null,
                target is null ? null : Limit(target, MaxIdLength)));
        }

        return tuners.OrderBy(t => t.Index).ToList();
    }

    private static int? TunerIndex(string? resource) =>
        resource is not null
        && resource.StartsWith(TunerResourcePrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(resource.AsSpan(TunerResourcePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;

    private static int? Percent(JsonElement element, string name) => Number(element, name) is { } value ? Math.Clamp(value, 0, 100) : null;

    private static long? Long(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// The lineup as channels. A channel the device marks as copy protected (<c>Tags</c> holds "drm") is
    /// left out, because nothing here can play it. The channel id is the guide number ("7.1"), which is unique
    /// on a device and is what the guide matcher reads.
    /// </summary>
    public static List<IptvChannel> ParseLineup(string json, Guid playlistId, Uri device)
    {
        var root = Root(json);
        if (root.ValueKind != JsonValueKind.Array) throw new IptvSourceException("The device's channel list isn't in the format an HDHomeRun uses.");

        var channels = new List<IptvChannel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var number = Text(entry, "GuideNumber");
            if (number is null || IsProtected(entry) || !seen.Add(number)) continue;

            channels.Add(new IptvChannel
            {
                PlaylistId = playlistId,
                ExternalChannelId = Limit(number, MaxIdLength),
                Name = Limit(Text(entry, "GuideName") ?? number, MaxNameLength),
                StreamUrl = StreamUrl(Text(entry, "URL"), number, device),
                Kind = IptvChannelKind.Tv,
                VideoCodec = CodecName(Text(entry, "VideoCodec"))
            });
        }

        return channels;
    }

    /// <summary>
    /// The codec a lineup entry names (<c>"VideoCodec":"MPEG2"</c>), as ffprobe spells it, or null when it
    /// names none or one that isn't recognised. It is only a hint: the channel is told apart as copy or
    /// encode from it before anything has been tuned, and playback replaces it with what it measures.
    /// </summary>
    internal static string? CodecName(string? listed) => listed?.Trim().ToUpperInvariant() switch
    {
        "MPEG2" or "MPEG-2" or "MPEG2VIDEO" => "mpeg2video",
        "H264" or "H.264" or "AVC" => "h264",
        "HEVC" or "H265" or "H.265" => "hevc",
        _ => null
    };

    /// <summary>
    /// The address to open a channel at. The device names it in the lineup; that is used unless it is missing,
    /// not a web address, or points at no real host, in which case it is built from the address the admin gave.
    /// </summary>
    internal static string StreamUrl(string? listed, string number, Uri device)
    {
        if (Uri.TryCreate(listed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            && uri.Host != "0.0.0.0")
        {
            return uri.ToString();
        }

        return $"{device.Scheme}://{device.Host}:{DefaultStreamPort.ToString(CultureInfo.InvariantCulture)}/auto/v{Uri.EscapeDataString(number)}";
    }

    private static bool IsProtected(JsonElement entry)
    {
        if (Text(entry, "Tags") is { } tags
            && tags.Split(',', StringSplitOptions.TrimEntries).Contains(DrmTag, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.TryGetProperty("DRM", out var drm)
            && (drm.ValueKind == JsonValueKind.True || (drm.ValueKind == JsonValueKind.Number && drm.TryGetInt32(out var flag) && flag != 0));
    }

    private static JsonElement Root(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new IptvSourceException("The address answered, but not like an HDHomeRun. Check that it is the device's own address.");
        }
    }

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int? Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
