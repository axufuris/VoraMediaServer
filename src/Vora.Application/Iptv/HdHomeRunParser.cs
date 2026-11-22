using System.Globalization;
using System.Text.Json;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record HdHomeRunDevice(string Name, string? Model, string? DeviceId, int? TunerCount);

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

    private const int MaxNameLength = 256;
    private const int MaxIdLength = 256;
    private const string DrmTag = "drm";

    public static HdHomeRunDevice ParseDevice(string json)
    {
        var root = Root(json);
        var model = Text(root, "ModelNumber");
        var name = Text(root, "FriendlyName") ?? model ?? "HDHomeRun";
        return new HdHomeRunDevice(Limit(name, MaxNameLength), model, Text(root, "DeviceID"), Number(root, "TunerCount") is > 0 and var tuners ? tuners : null);
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
