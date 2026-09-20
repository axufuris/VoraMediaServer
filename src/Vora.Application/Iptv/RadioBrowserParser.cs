using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

internal static class RadioBrowserParser
{
    public static List<IptvChannel> Parse(string json, Guid playlistId, IptvChannelKind defaultKind)
    {
        var channels = new List<IptvChannel>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return channels;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var channel = ParseStation(element, playlistId, defaultKind);
            if (channel != null) channels.Add(channel);
        }

        return channels;
    }

    private static IptvChannel? ParseStation(JsonElement element, Guid playlistId, IptvChannelKind defaultKind)
    {
        var streamUrl = GetString(element, "url_resolved") ?? GetString(element, "url");
        if (string.IsNullOrWhiteSpace(streamUrl)) return null;

        var stationUuid = GetString(element, "stationuuid");
        var name = GetString(element, "name") ?? "Unknown Station";
        var favicon = GetString(element, "favicon");
        var tags = GetString(element, "tags");
        var country = GetString(element, "country");
        var countryCode = GetString(element, "countrycode");

        var externalId = !string.IsNullOrEmpty(stationUuid)
            ? stationUuid
            : DeriveFallbackExternalId(streamUrl);

        var resolvedKind = defaultKind == IptvChannelKind.Tv ? IptvChannelKind.Radio : defaultKind;

        return new IptvChannel
        {
            PlaylistId = playlistId,
            ExternalChannelId = Truncate(externalId, 256),
            Name = Truncate(name, 256),
            LogoUrl = TruncateOrNull(IsHttpUrl(favicon) ? favicon : null, 1024),
            GroupTitle = TruncateOrNull(BuildGroupTitle(tags, country), 128),
            StreamUrl = Truncate(streamUrl, 1024),
            Resolution = null,
            CountryCode = TruncateOrNull(countryCode?.ToUpperInvariant(), 8),
            Kind = resolvedKind
        };
    }

    private static string? BuildGroupTitle(string? tags, string? country)
    {
        if (!string.IsNullOrEmpty(tags))
        {
            var firstTag = tags.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstTag))
            {
                return TitleCase(firstTag);
            }
        }
        return string.IsNullOrEmpty(country) ? null : country;
    }

    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var cleaned = s.Replace("'", string.Empty).Replace("’", string.Empty);
        if (string.IsNullOrEmpty(cleaned)) return cleaned;
        var lower = cleaned.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static bool IsHttpUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var s = prop.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static string DeriveFallbackExternalId(string streamUrl)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(streamUrl));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? TruncateOrNull(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
