using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Vora.Application.Iptv.Dtos;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

public class XmlTvParseStats
{
    public int ProgrammesSeen { get; set; }
    public int ProgrammesMatched { get; set; }
    public HashSet<string> UnmatchedIdSamples { get; } = new(StringComparer.OrdinalIgnoreCase);
    public const int UnmatchedSampleLimit = 10;
}

internal static class XmlTvParser
{
    private const string UnknownProgramTitle = "Unknown Program";
    private const string UnratedRating = "NR";
    private const string XmltvNsSystem = "xmltv_ns";

    private static readonly Regex SeasonEpisodeRegex = new(@"[Ss](\d+)\s*[Ee](\d+)", RegexOptions.Compiled);

    public static async Task<XmlTvParseStats> ParseAsync(Stream stream, List<IptvChannel> knownChannels, Dictionary<string, List<IptvProgramDto>> updatedCache, DateTime cutoffTime, DateTime maxFutureTime, CancellationToken cancellationToken)
    {
        var idMap = BuildKnownIdMap(knownChannels);
        var stats = new XmlTvParseStats();

        var xmlSettings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(stream, xmlSettings);

        while (await reader.ReadAsync())
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.Name == "programme")
            {
                await ParseProgrammeElementAsync(reader, idMap, updatedCache, cutoffTime, maxFutureTime, stats);
            }
        }

        return stats;
    }

    private static Dictionary<string, List<string>> BuildKnownIdMap(List<IptvChannel> knownChannels)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in knownChannels)
        {
            if (string.IsNullOrWhiteSpace(channel.ExternalChannelId)) continue;

            var canonical = channel.ExternalChannelId;
            AddIndex(map, canonical, canonical);
            AddIndex(map, NormalizeId(canonical), canonical);

            var stripped = StripQualitySuffix(canonical);
            if (!string.Equals(stripped, canonical, StringComparison.Ordinal))
            {
                AddIndex(map, stripped, canonical);
                AddIndex(map, NormalizeId(stripped), canonical);
            }
        }

        return map;
    }

    private static void AddIndex(Dictionary<string, List<string>> map, string key, string canonical)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }
        if (!list.Contains(canonical, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(canonical);
        }
    }

    private static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        return new string(id.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string StripQualitySuffix(string id)
    {
        var atIndex = id.IndexOf('@');
        return atIndex > 0 ? id.Substring(0, atIndex) : id;
    }

    private static async Task ParseProgrammeElementAsync(XmlReader reader, Dictionary<string, List<string>> idMap, Dictionary<string, List<IptvProgramDto>> updatedCache, DateTime cutoffTime, DateTime maxFutureTime, XmlTvParseStats stats)
    {
        var xmlChannelId = reader.GetAttribute("channel")?.Trim();
        stats.ProgrammesSeen++;

        if (string.IsNullOrEmpty(xmlChannelId))
        {
            await reader.SkipAsync();
            return;
        }

        if (!idMap.TryGetValue(xmlChannelId, out var canonicalIds))
        {
            var normalized = NormalizeId(xmlChannelId);
            if (string.IsNullOrEmpty(normalized) || !idMap.TryGetValue(normalized, out canonicalIds))
            {
                if (stats.UnmatchedIdSamples.Count < XmlTvParseStats.UnmatchedSampleLimit)
                {
                    stats.UnmatchedIdSamples.Add(xmlChannelId);
                }
                await reader.SkipAsync();
                return;
            }
        }

        stats.ProgrammesMatched++;

        var startTime = ParseXmlTvDate(reader.GetAttribute("start"));
        var stopTime = ParseXmlTvDate(reader.GetAttribute("stop"));

        if (stopTime < cutoffTime || startTime > maxFutureTime)
        {
            await reader.SkipAsync();
            return;
        }

        var template = new IptvProgramDto
        {
            Id = string.Empty,
            ChannelId = string.Empty,
            StartTime = startTime,
            EndTime = stopTime,
            Title = UnknownProgramTitle
        };

        await PopulateProgramAsync(reader, template);
        ApplyTitleEpisodeFallback(template);

        foreach (var canonicalId in canonicalIds)
        {
            var program = new IptvProgramDto
            {
                Id = GenerateDeterministicProgramId(canonicalId, startTime),
                ChannelId = canonicalId,
                StartTime = template.StartTime,
                EndTime = template.EndTime,
                Title = template.Title,
                Description = template.Description,
                ContentRating = template.ContentRating,
                SeasonNumber = template.SeasonNumber,
                EpisodeNumber = template.EpisodeNumber
            };

            if (!updatedCache.ContainsKey(canonicalId)) updatedCache[canonicalId] = new List<IptvProgramDto>();
            updatedCache[canonicalId].Add(program);
        }
    }

    private static async Task PopulateProgramAsync(XmlReader reader, IptvProgramDto program)
    {
        var currentElement = string.Empty;
        var currentSystem = string.Empty;
        using var innerReader = reader.ReadSubtree();

        while (await innerReader.ReadAsync())
        {
            if (innerReader.NodeType == XmlNodeType.Element)
            {
                currentElement = innerReader.Name;
                if (currentElement == "episode-num") currentSystem = innerReader.GetAttribute("system") ?? string.Empty;
            }
            else if (innerReader.NodeType == XmlNodeType.EndElement)
            {
                currentElement = string.Empty;
                currentSystem = string.Empty;
            }
            else if (innerReader.NodeType == XmlNodeType.Text || innerReader.NodeType == XmlNodeType.CDATA)
            {
                if (currentElement == "title") program.Title = innerReader.Value.Trim();
                else if (currentElement == "desc") program.Description = innerReader.Value.Trim();
                else if (currentElement == "value") program.ContentRating = NormalizeRating(innerReader.Value);
                else if (currentElement == "episode-num" && currentSystem == XmltvNsSystem)
                {
                    var parts = innerReader.Value.Split('.');
                    if (parts.Length > 0 && int.TryParse(parts[0].Split('/')[0], out var s)) program.SeasonNumber = s + 1;
                    if (parts.Length > 1 && int.TryParse(parts[1].Split('/')[0], out var e)) program.EpisodeNumber = e + 1;
                }
            }
        }
    }

    private static void ApplyTitleEpisodeFallback(IptvProgramDto program)
    {
        if (program.SeasonNumber.HasValue && program.EpisodeNumber.HasValue) return;

        var textToSearch = $"{program.Title} {program.Description}";
        var match = SeasonEpisodeRegex.Match(textToSearch);
        if (!match.Success) return;

        if (int.TryParse(match.Groups[1].Value, out var s)) program.SeasonNumber = s;
        if (int.TryParse(match.Groups[2].Value, out var e)) program.EpisodeNumber = e;
    }

    private static string NormalizeRating(string? rawRating)
    {
        if (string.IsNullOrWhiteSpace(rawRating)) return UnratedRating;

        var r = rawRating.ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);

        if (r.Contains("TVMA") || r == "MA" || r == "18+") return "TV-MA";
        if (r.Contains("TV14") || r == "14+") return "TV-14";
        if (r.Contains("TVPG") || r == "PG") return "TV-PG";
        if (r.Contains("TVG") || r == "G") return "TV-G";
        if (r.Contains("TVY7")) return "TV-Y7";
        if (r.Contains("TVY")) return "TV-Y";
        if (r == "R") return "R";
        if (r == "PG13") return "PG-13";

        return UnratedRating;
    }

    private static DateTime ParseXmlTvDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.UtcNow;

        var cleanStr = dateStr.Replace(" ", string.Empty);

        if (cleanStr.Length < 14) return DateTime.UtcNow;
        if (!DateTime.TryParseExact(cleanStr.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return DateTime.UtcNow;
        }

        if (cleanStr.Length >= 19)
        {
            var sign = cleanStr[14];
            if (int.TryParse(cleanStr.Substring(15, 2), out var hours) && int.TryParse(cleanStr.Substring(17, 2), out var minutes))
            {
                var offset = new TimeSpan(hours, minutes, 0);
                dt = sign == '+' ? dt.Subtract(offset) : dt.Add(offset);
            }
        }

        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    private static string GenerateDeterministicProgramId(string channelId, DateTime startTime)
    {
        var inputBytes = Encoding.UTF8.GetBytes($"{channelId}_{startTime.Ticks}");
        var hashBytes = MD5.HashData(inputBytes);
        return new Guid(hashBytes).ToString();
    }
}
