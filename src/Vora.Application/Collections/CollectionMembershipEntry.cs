using System.Globalization;
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
}
