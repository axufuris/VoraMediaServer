using System.Globalization;
using System.Linq;
using System.Text;

namespace Vora.Application.Collections;

public class CollectionMembershipEntry
{
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string MediaType { get; set; } = "Movie";

    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
}

public static class TitleMatch
{
    // Normalize a title for fuzzy equality: lowercase, strip diacritics, drop
    // any character that isn't a letter/digit/space, then collapse whitespace.
    // Matching is always paired with year + type by the caller, since titles
    // alone collide across a movie and a show (or reboots).
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var decomposed = title.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    // The set of normalized forms a title can match on. Always includes the
    // plain normalized title, plus a variant with a leading possessive prefix
    // removed so a library entry stored under its official studio title (e.g.
    // "Marvel's Agent Carter" -> "marvel s agent carter") also matches a short
    // title an AI or list uses ("Agent Carter" -> "agent carter"). The
    // apostrophe-s of a possessive normalizes to a standalone "s" token, so the
    // prefix appears as "<word> s <rest>".
    public static IEnumerable<string> MatchKeys(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            yield break;
        }

        var normalized = Normalize(title);
        if (normalized.Length == 0)
        {
            yield break;
        }

        yield return normalized;

        var parts = normalized.Split(' ');
        if (parts.Length >= 3 && parts[1] == "s")
        {
            var stripped = string.Join(' ', parts.Skip(2));
            if (stripped.Length > 0 && stripped != normalized)
            {
                yield return stripped;
            }
        }

        if (parts.Length >= 2 && parts[^1].Length == 4 && parts[^1].All(char.IsDigit))
        {
            var withoutYear = string.Join(' ', parts[..^1]);
            if (withoutYear.Length > 0)
            {
                yield return withoutYear;
            }
        }

        var colon = title.IndexOf(':');
        if (colon > 0 && colon < title.Length - 1)
        {
            var normalizedPrefix = Normalize(title[..colon]);
            if (DesignationPrefixes.Any(d => normalizedPrefix.Contains(d)))
            {
                var suffix = Normalize(title[(colon + 1)..]);
                if (suffix.Length > 0 && suffix != normalized)
                {
                    yield return suffix;
                }
            }
        }
    }

    private static readonly string[] DesignationPrefixes = { "one shot", "presents" };
}
