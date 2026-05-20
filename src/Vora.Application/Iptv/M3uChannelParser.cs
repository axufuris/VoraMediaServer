using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

internal static class M3uChannelParser
{
    private const string ExtInfMarker = "#EXTINF";
    private const string DefaultChannelName = "Unknown Channel";

    private static readonly Regex TvgIdRegex = new(@"tvg-id=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChannelIdRegex = new(@"channel-id=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TvgLogoRegex = new(@"tvg-logo=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GroupTitleRegex = new(@"group-title=""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RadioAttrRegex = new(@"radio=""(true|1|yes)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TvgTypeAudioRegex = new(@"tvg-type=""audio""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RadioGroupRegex = new(@"\b(radio|music|fm|am)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdCountryRegex = new(@"\.([a-zA-Z]{2})@", RegexOptions.Compiled);
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
        var streamUrl = FindStreamUrl(lines, currentIndex);
        if (string.IsNullOrWhiteSpace(streamUrl)) return null;

        var extId = ResolveExternalId(line);
        if (string.IsNullOrWhiteSpace(extId))
        {
            extId = DeriveFallbackExternalId(streamUrl);
        }

        var rawName = ExtractRawName(line);
        var (resolution, countryCode, cleanedName) = ExtractMetadata(extId, rawName);

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
            Resolution = TruncateOrNull(resolution, 16),
            CountryCode = TruncateOrNull(countryCode, 8),
            Kind = DetectKind(line, groupTitle, resolution, streamUrl, defaultKind)
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? TruncateOrNull(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);

    private static IptvChannelKind DetectKind(string extInfLine, string? groupTitle, string? resolution, string streamUrl, IptvChannelKind defaultKind)
    {
        if (RadioAttrRegex.IsMatch(extInfLine)) return IptvChannelKind.Radio;
        if (TvgTypeAudioRegex.IsMatch(extInfLine)) return IptvChannelKind.Radio;
        if (!string.IsNullOrEmpty(groupTitle) && RadioGroupRegex.IsMatch(groupTitle)) return IptvChannelKind.Radio;
        if (string.Equals(resolution, "audio", StringComparison.OrdinalIgnoreCase)) return IptvChannelKind.Radio;
        if (LooksLikeAudioStream(streamUrl)) return IptvChannelKind.Radio;
        return defaultKind;
    }

    private static bool LooksLikeAudioStream(string url)
    {
        var path = url;
        var queryIdx = url.IndexOf('?');
        if (queryIdx >= 0) path = url[..queryIdx];
        return path.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pls", StringComparison.OrdinalIgnoreCase);
    }

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

    private static (string? Resolution, string? CountryCode, string CleanedName) ExtractMetadata(string extId, string rawName)
    {
        string? resolution = null;
        string? countryCode = null;

        var idCountryMatch = IdCountryRegex.Match(extId);
        if (idCountryMatch.Success) countryCode = idCountryMatch.Groups[1].Value.ToUpper();

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

        return (resolution, countryCode, cleanedName);
    }

    private static string FindStreamUrl(string[] lines, int currentIndex)
    {
        for (var j = currentIndex + 1; j < lines.Length; j++)
        {
            if (!string.IsNullOrWhiteSpace(lines[j]) && !lines[j].StartsWith("#"))
            {
                return lines[j].Trim();
            }
        }
        return string.Empty;
    }
}
