using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

internal static class M3uChannelParser
{
    private const string ExtInfMarker = "#EXTINF";
    private const string VlcOptionMarker = "#EXTVLCOPT:";
    private const string HttpHeadersMarker = "#EXTHTTP:";
    private const string KodiPropertyMarker = "#KODIPROP:";
    private const string KodiStreamHeadersProperty = "inputstream.adaptive.stream_headers";
    private const string DefaultChannelName = "Unknown Channel";

    private static readonly Regex TvgIdRegex = new(@"tvg-id=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChannelIdRegex = new(@"channel-id=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TvgLogoRegex = new(@"tvg-logo=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GroupTitleRegex = new(@"group-title=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TvgCountryRegex = new(@"tvg-country=""([a-zA-Z]{2})""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdCountryRegex = new(@"\.(US|UK|GB|CA|AU|NZ|IE|FR|DE|ES|IT|MX|BR|AR|GR|IN|RU|JP|KR|CN|NL|BE|PT|PL|SE|NO|DK|FI|CH|AT|ZA)(?:@|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdResolutionRegex = new(@"@(1080p|720p|480p|fhd|hd|sd|4k)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NameResolutionRegex = new(@"(?i)\b(1080p|720p|480p|fhd|hd|sd|4k)\b", RegexOptions.Compiled);
    private static readonly Regex NameCountryRegex = new(@"(?i)\b(US|UK|CA|AU|FR|DE|ES|IT|MX|GR|IN|RU)\b", RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"[\(\)\[\]]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static List<IptvChannel> Parse(string m3uContent, Guid playlistId, IptvChannelKind defaultKind = IptvChannelKind.Tv)
    {
        var channels = new List<IptvChannel>();
        var lines = m3uContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.StartsWith(ExtInfMarker)) continue;

            var channel = ParseChannelLine(line, lines, i, playlistId, defaultKind);
            if (channel != null)
            {
                channels.Add(channel);
            }
        }

        return channels;
    }

    private static IptvChannel? ParseChannelLine(string line, string[] lines, int currentIndex, Guid playlistId, IptvChannelKind defaultKind)
    {
        var entry = FindStream(lines, currentIndex);
        if (string.IsNullOrWhiteSpace(entry.RawUrl)) return null;

        var (streamUrl, suffixHeaders) = SplitHeaderSuffix(entry.RawUrl);
        if (string.IsNullOrWhiteSpace(streamUrl)) return null;

        var extId = ResolveExternalId(line);
        if (string.IsNullOrWhiteSpace(extId))
        {
            extId = DeriveFallbackExternalId(entry.RawUrl);
        }

        var rawName = ExtractRawName(line);
        var (resolution, countryCode, cleanedName) = ExtractMetadata(extId, rawName, line);

        var logoMatch = TvgLogoRegex.Match(line);
        var groupMatch = GroupTitleRegex.Match(line);
        var groupTitle = groupMatch.Success ? groupMatch.Groups[1].Value : null;

        return new IptvChannel
        {
            PlaylistId = playlistId,
            ExternalChannelId = Truncate(extId, 256),
            Name = Truncate(cleanedName, 256),
            LogoUrl = TruncateOrNull(logoMatch.Success ? logoMatch.Groups[1].Value : null, 1024),
            GroupTitle = TruncateOrNull(groupTitle, 128),
            StreamUrl = streamUrl,
            UserAgent = IptvRequestPolicy.CleanHeaderValue(suffixHeaders.UserAgent ?? entry.Headers.UserAgent, IptvRequestPolicy.MaxUserAgentLength),
            Referer = IptvRequestPolicy.CleanHeaderValue(suffixHeaders.Referer ?? entry.Headers.Referer, IptvRequestPolicy.MaxRefererLength),
            Resolution = TruncateOrNull(resolution, 16),
            CountryCode = TruncateOrNull(countryCode, 8),
            Kind = defaultKind
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? TruncateOrNull(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);

    private static string ResolveExternalId(string line)
    {
        var idMatch = TvgIdRegex.Match(line);
        if (idMatch.Success && !string.IsNullOrWhiteSpace(idMatch.Groups[1].Value))
        {
            return idMatch.Groups[1].Value;
        }

        var chIdMatch = ChannelIdRegex.Match(line);
        return chIdMatch.Success ? chIdMatch.Groups[1].Value : string.Empty;
    }

    private static string DeriveFallbackExternalId(string streamUrl)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(streamUrl));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ExtractRawName(string line)
    {
        var nameIndex = line.LastIndexOf(',');
        return nameIndex >= 0 && nameIndex < line.Length - 1
            ? line.Substring(nameIndex + 1).Trim()
            : DefaultChannelName;
    }

    internal static (string? Resolution, string? CountryCode, string CleanedName) ExtractMetadata(string extId, string rawName, string rawLine)
    {
        string? resolution = null;
        string? countryCode = null;

        var tvgCountryMatch = TvgCountryRegex.Match(rawLine);
        if (tvgCountryMatch.Success) countryCode = tvgCountryMatch.Groups[1].Value.ToUpper();

        if (string.IsNullOrEmpty(countryCode))
        {
            var idCountryMatch = IdCountryRegex.Match(extId);
            if (idCountryMatch.Success) countryCode = idCountryMatch.Groups[1].Value.ToUpper();
        }

        var idResMatch = IdResolutionRegex.Match(extId);
        if (idResMatch.Success) resolution = idResMatch.Groups[1].Value.ToUpper();

        var resMatch = NameResolutionRegex.Match(rawName);
        if (resMatch.Success)
        {
            if (string.IsNullOrEmpty(resolution)) resolution = resMatch.Groups[1].Value.ToUpper();
            rawName = NameResolutionRegex.Replace(rawName, string.Empty).Trim();
        }

        var countryMatch = NameCountryRegex.Match(rawName);
        if (countryMatch.Success)
        {
            if (string.IsNullOrEmpty(countryCode)) countryCode = countryMatch.Groups[1].Value.ToUpper();
            rawName = NameCountryRegex.Replace(rawName, string.Empty).Trim();
        }

        rawName = BracketRegex.Replace(rawName, string.Empty);
        var cleanedName = WhitespaceRegex.Replace(rawName, " ").Trim(' ', '-');

        if (string.Equals(countryCode, "GB", StringComparison.OrdinalIgnoreCase))
        {
            countryCode = "UK";
        }

        return (resolution, countryCode, cleanedName);
    }

    private sealed record StreamHeaders(string? UserAgent, string? Referer)
    {
        public static readonly StreamHeaders None = new(null, null);

        public StreamHeaders Merge(StreamHeaders other) => new(other.UserAgent ?? UserAgent, other.Referer ?? Referer);
    }

    private sealed record StreamEntry(string RawUrl, StreamHeaders Headers);

    private static StreamEntry FindStream(string[] lines, int currentIndex)
    {
        var headers = StreamHeaders.None;
        for (var j = currentIndex + 1; j < lines.Length; j++)
        {
            if (string.IsNullOrWhiteSpace(lines[j])) continue;

            var line = lines[j].Trim();
            if (!line.StartsWith('#'))
            {
                return new StreamEntry(line, headers);
            }

            headers = headers.Merge(ReadOptionLine(line));
        }
        return new StreamEntry(string.Empty, StreamHeaders.None);
    }

    private static StreamHeaders ReadOptionLine(string line)
    {
        if (line.StartsWith(VlcOptionMarker, StringComparison.OrdinalIgnoreCase))
        {
            var option = line[VlcOptionMarker.Length..].Trim();
            var eq = option.IndexOf('=');
            if (eq <= 0) return StreamHeaders.None;

            var name = option[..eq].Trim();
            var value = option[(eq + 1)..].Trim().Trim('"');
            if (string.Equals(name, "http-user-agent", StringComparison.OrdinalIgnoreCase)) return new StreamHeaders(value, null);
            if (string.Equals(name, "http-referrer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "http-referer", StringComparison.OrdinalIgnoreCase))
            {
                return new StreamHeaders(null, value);
            }
            return StreamHeaders.None;
        }

        if (line.StartsWith(HttpHeadersMarker, StringComparison.OrdinalIgnoreCase))
        {
            return ReadJsonHeaders(line[HttpHeadersMarker.Length..]);
        }

        if (line.StartsWith(KodiPropertyMarker, StringComparison.OrdinalIgnoreCase))
        {
            var property = line[KodiPropertyMarker.Length..].Trim();
            var eq = property.IndexOf('=');
            if (eq > 0 && string.Equals(property[..eq].Trim(), KodiStreamHeadersProperty, StringComparison.OrdinalIgnoreCase))
            {
                return ReadHeaderPairs(property[(eq + 1)..]);
            }
        }

        return StreamHeaders.None;
    }

    private static StreamHeaders ReadJsonHeaders(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return StreamHeaders.None;

            string? userAgent = null;
            string? referer = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                if (IsUserAgentName(property.Name)) userAgent = property.Value.GetString();
                else if (IsRefererName(property.Name)) referer = property.Value.GetString();
            }
            return new StreamHeaders(userAgent, referer);
        }
        catch (JsonException)
        {
            return StreamHeaders.None;
        }
    }

    private static (string Url, StreamHeaders Headers) SplitHeaderSuffix(string rawUrl)
    {
        var pipe = rawUrl.IndexOf('|');
        if (pipe < 0) return (rawUrl, StreamHeaders.None);

        var suffix = rawUrl[(pipe + 1)..];
        if (!suffix.Contains('=')) return (rawUrl, StreamHeaders.None);

        return (rawUrl[..pipe].Trim(), ReadHeaderPairs(suffix));
    }

    private static StreamHeaders ReadHeaderPairs(string pairs)
    {
        string? userAgent = null;
        string? referer = null;
        foreach (var pair in pairs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var name = pair[..eq].Trim();
            var value = Unescape(pair[(eq + 1)..].Trim());
            if (IsUserAgentName(name)) userAgent = value;
            else if (IsRefererName(name)) referer = value;
        }
        return new StreamHeaders(userAgent, referer);
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static bool IsUserAgentName(string name) =>
        string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "user_agent", StringComparison.OrdinalIgnoreCase);

    private static bool IsRefererName(string name) =>
        string.Equals(name, "Referer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Referrer", StringComparison.OrdinalIgnoreCase);
}
