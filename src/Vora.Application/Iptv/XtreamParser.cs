using System.Globalization;
using System.Text.Json;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record XtreamAccountInfo(
    string Status,
    DateTime? ExpiresAt,
    int? MaxConnections,
    int? ActiveConnections,
    bool IsTrial,
    IReadOnlyList<string> AllowedOutputFormats);

public static class XtreamParser
{
    public const string ActiveStatus = "Active";
    public const string HlsExtension = "m3u8";
    public const string TransportStreamExtension = "ts";
    public const string FallbackIdPrefix = "xtream-";
    public const string RejectedMessage = "The provider rejected the username or password.";

    private const int MaxExternalIdLength = 256;
    private const int MaxNameLength = 256;
    private const int MaxLogoLength = 1024;
    private const int MaxGroupLength = 128;
    private const string RadioStreamType = "radio_streams";
    private const string UnknownChannelName = "Unknown Channel";
    private static readonly char[] NameSeparators = { ' ', '-', ':', '|', '/', '.' };

    public static XtreamAccountInfo ParseAccount(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new IptvSourceException("The server didn't answer like an Xtream Codes panel. Check the server address.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("user_info", out var user)
                || user.ValueKind != JsonValueKind.Object)
            {
                throw new XtreamAuthenticationException(RejectedMessage);
            }

            if (ReadInt(user, "auth") != 1)
            {
                throw new XtreamAuthenticationException(RejectedMessage);
            }

            return new XtreamAccountInfo(
                ReadString(user, "status") ?? ActiveStatus,
                ReadUnixTime(user, "exp_date"),
                ReadInt(user, "max_connections"),
                ReadInt(user, "active_cons"),
                ReadInt(user, "is_trial") == 1,
                ReadStringArray(user, "allowed_output_formats"));
        }
    }

    public static string? AccountProblem(XtreamAccountInfo info, DateTime now)
    {
        if (!string.Equals(info.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            return info.Status.ToLowerInvariant() switch
            {
                "expired" => "The provider account has expired.",
                "banned" => "The provider account is banned.",
                "disabled" => "The provider account is disabled.",
                _ => $"The provider account is not active (status: {Truncate(info.Status, 32)})."
            };
        }

        if (info.ExpiresAt is { } expiry && expiry <= now)
        {
            return $"The provider account expired on {expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";
        }

        return null;
    }

    public static string PreferredExtension(IReadOnlyList<string> allowedOutputFormats) =>
        allowedOutputFormats.Any(f => string.Equals(f, HlsExtension, StringComparison.OrdinalIgnoreCase))
            ? HlsExtension
            : TransportStreamExtension;

    public static Dictionary<string, string> ParseCategories(string json)
    {
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = ParseArray(json, "categories");
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(element, "category_id");
            var name = ReadString(element, "category_name");
            if (id is null || name is null) continue;
            categories[id] = name.Trim();
        }

        return categories;
    }

    public static List<IptvChannel> ParseLiveStreams(string json, Guid playlistId, IptvChannelKind defaultKind, IReadOnlyDictionary<string, string> categories, string extension)
    {
        using var document = ParseArray(json, "channel list");

        var streams = new List<XtreamStream>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var streamId = ReadString(element, "stream_id");
            if (streamId is null || !IsSafeStreamId(streamId)) continue;

            streams.Add(new XtreamStream(
                streamId,
                ReadString(element, "name") ?? UnknownChannelName,
                ReadString(element, "stream_icon"),
                ReadString(element, "epg_channel_id")?.Trim(),
                ReadString(element, "category_id") ?? FirstOf(element, "category_ids"),
                string.Equals(ReadString(element, "stream_type"), RadioStreamType, StringComparison.OrdinalIgnoreCase)));
        }

        var bareIdOwners = streams
            .Where(s => !string.IsNullOrEmpty(s.EpgChannelId))
            .GroupBy(s => s.EpgChannelId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StreamId, StreamIdComparer.Instance).First().StreamId, StringComparer.OrdinalIgnoreCase);

        var channels = new List<IptvChannel>(streams.Count);
        foreach (var stream in streams)
        {
            var externalId = ExternalIdFor(stream, bareIdOwners);
            var (resolution, countryCode, cleanedName) = M3uChannelParser.ExtractMetadata(stream.EpgChannelId ?? string.Empty, stream.Name, string.Empty);
            var name = cleanedName.Trim(NameSeparators);

            channels.Add(new IptvChannel
            {
                PlaylistId = playlistId,
                ExternalChannelId = externalId,
                Name = Truncate(name.Length == 0 ? stream.Name.Trim() : name, MaxNameLength),
                LogoUrl = IsHttpUrl(stream.Icon) ? Truncate(stream.Icon!, MaxLogoLength) : null,
                GroupTitle = stream.CategoryId is not null && categories.TryGetValue(stream.CategoryId, out var group) ? Truncate(group, MaxGroupLength) : null,
                StreamUrl = XtreamStreamReference.For(stream.StreamId, extension),
                Resolution = resolution is null ? null : Truncate(resolution, 16),
                CountryCode = countryCode is null ? null : Truncate(countryCode, 8),
                Kind = stream.IsRadio ? IptvChannelKind.Radio : defaultKind
            });
        }

        return channels;
    }

    private sealed record XtreamStream(string StreamId, string Name, string? Icon, string? EpgChannelId, string? CategoryId, bool IsRadio);

    private sealed class StreamIdComparer : IComparer<string>
    {
        public static readonly StreamIdComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                && long.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return a.CompareTo(b);
            }
            return string.CompareOrdinal(x, y);
        }
    }

    private static string ExternalIdFor(XtreamStream stream, IReadOnlyDictionary<string, string> bareIdOwners)
    {
        if (string.IsNullOrEmpty(stream.EpgChannelId))
        {
            return Truncate(FallbackIdPrefix + stream.StreamId, MaxExternalIdLength);
        }

        if (bareIdOwners.TryGetValue(stream.EpgChannelId, out var owner) && owner == stream.StreamId)
        {
            return Truncate(stream.EpgChannelId, MaxExternalIdLength);
        }

        var suffix = "@" + stream.StreamId;
        return Truncate(stream.EpgChannelId, MaxExternalIdLength - suffix.Length) + suffix;
    }

    private static JsonDocument ParseArray(string json, string what)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new IptvSourceException($"The provider's {what} could not be read.");
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array) return document;

        document.Dispose();
        throw new IptvSourceException($"The provider's {what} could not be read.");
    }

    private static bool IsSafeStreamId(string streamId) =>
        streamId.Length is > 0 and <= 64 && streamId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string? FirstOf(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in value.EnumerateArray())
        {
            var text = Text(item);
            if (text is not null) return text;
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? Text(value) : null;

    private static string? Text(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        var text = ReadString(element, property);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTime? ReadUnixTime(JsonElement element, string property)
    {
        var text = ReadString(element, property);
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray().Select(Text).OfType<string>().ToList();
    }

    private static bool IsHttpUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
