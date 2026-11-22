using System.Text.RegularExpressions;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

/// <summary>
/// Finds a tuner channel in an XMLTV guide that does not use its guide number as an id. An antenna lineup
/// has no guide ids, and the feeds people use for it (Schedules Direct, zap2it grabbers) name a channel by
/// its number and call sign in <c>&lt;display-name&gt;</c> instead. A channel matches on its number first
/// ("7.1" or "7.1 WABC"), else on its call sign with a trailing HD, DT or TV ignored ("WABC-HD" is "WABC-DT").
/// Only tuner channels are matched this way: exact names collide too easily across ordinary playlists.
/// </summary>
internal sealed partial class TunerGuideAliases
{
    private static readonly string[] CallSignSuffixes = ["hd", "dt", "tv", "ld", "cd"];
    private const int MinimumCallSignLength = 3;

    private readonly Dictionary<string, List<string>> _byNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _byCallSign = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _callSignOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _claims = new(StringComparer.OrdinalIgnoreCase);

    public TunerGuideAliases(IEnumerable<IptvChannel> channels)
    {
        foreach (var channel in channels)
        {
            if (string.IsNullOrWhiteSpace(channel.ExternalChannelId)) continue;
            var id = channel.ExternalChannelId;
            Add(_byNumber, id.Trim(), id);
            if (CallSign(channel.Name) is { } callSign)
            {
                Add(_byCallSign, callSign, id);
                _callSignOf[id] = callSign;
            }
        }
    }

    /// <summary>Records that an XMLTV channel already supplies these channels by id, so no name match takes them.</summary>
    public void Claim(string xmlChannelId, IEnumerable<string> channelIds)
    {
        foreach (var id in channelIds) _claims.TryAdd(id, xmlChannelId);
    }

    /// <summary>
    /// The channels an XMLTV channel with these display names stands for. A channel already given to an
    /// earlier XMLTV channel is not given again, so two feeds' worth of the same number cannot stack programmes.
    /// </summary>
    public List<string> Resolve(string xmlChannelId, IReadOnlyCollection<string> displayNames)
    {
        var found = FindByNumber(displayNames);
        if (found.Count == 0) found = FindByCallSign(displayNames);

        var free = found.Where(id => _claims.TryAdd(id, xmlChannelId) || string.Equals(_claims[id], xmlChannelId, StringComparison.OrdinalIgnoreCase)).ToList();
        return free;
    }

    // A number with a dot is an antenna virtual channel (7.1) and is distinctive enough alone. A plain number
    // (5) is also the start of names like "5 Star", so it needs the whole name to be the number or the call sign to agree.
    private List<string> FindByNumber(IReadOnlyCollection<string> displayNames)
    {
        var found = new List<string>();
        foreach (var name in displayNames)
        {
            if (LeadingNumber(name) is not { } number || !_byNumber.TryGetValue(number, out var ids)) continue;

            var rest = LeadingNumberRegex().Replace(name, string.Empty).Trim();
            foreach (var id in ids)
            {
                var distinctive = number.Contains('.') || rest.Length == 0
                    || (CallSign(rest) is { } callSign && _callSignOf.TryGetValue(id, out var own) && own == callSign);
                if (distinctive) found.Add(id);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> FindByCallSign(IReadOnlyCollection<string> displayNames)
    {
        var found = new List<string>();
        foreach (var name in displayNames)
        {
            var withoutNumber = LeadingNumberRegex().Replace(name, string.Empty);
            if (CallSign(withoutNumber) is { } callSign && _byCallSign.TryGetValue(callSign, out var ids)) found.AddRange(ids);
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string? LeadingNumber(string displayName)
    {
        var match = LeadingNumberRegex().Match(displayName);
        return match.Success ? match.Groups["number"].Value : null;
    }

    internal static string? CallSign(string name)
    {
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        foreach (var suffix in CallSignSuffixes)
        {
            if (letters.EndsWith(suffix, StringComparison.Ordinal) && letters.Length - suffix.Length >= MinimumCallSignLength)
            {
                letters = letters[..^suffix.Length];
                break;
            }
        }
        return letters.Length >= MinimumCallSignLength ? letters : null;
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string id)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(id, StringComparer.OrdinalIgnoreCase)) list.Add(id);
    }

    [GeneratedRegex(@"^\s*(?<number>\d{1,4}(?:\.\d{1,3})?)(?=$|[\s\-_.])\s*[-_.]?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumberRegex();
}
