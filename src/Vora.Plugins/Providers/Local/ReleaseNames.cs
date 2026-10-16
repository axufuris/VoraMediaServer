using System.Globalization;
using System.Text.RegularExpressions;

namespace Vora.Plugins.Providers.Local;

public static partial class ReleaseNames
{
    public static (string Title, int? Year)? Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var dotted = !name.Contains(' ') && Separator().IsMatch(name);
        var tokens = (dotted ? Separator().Replace(name, " ") : name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        var tag = Array.FindIndex(tokens, 1, token => ReleaseTag().IsMatch(token));
        var end = tag >= 0 ? tag : tokens.Length;
        var yearAt = dotted || tag >= 0 ? LastYearBefore(tokens, end) : null;
        if (tag < 0 && yearAt == null) return null;

        var titleTokens = tokens[..(yearAt ?? end)];
        var title = dotted ? JoinDotted(titleTokens) : string.Join(' ', titleTokens);
        return title.Length == 0 ? null : (title, yearAt is int at ? YearOf(tokens[at]) : null);
    }

    private static string JoinDotted(string[] tokens)
    {
        var words = new List<string>();
        var letters = new List<string>();
        foreach (var token in tokens.Append(string.Empty))
        {
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                letters.Add(token);
                continue;
            }

            if (letters.Count > 1) words.Add(string.Concat(letters.Select(letter => letter + ".")));
            else words.AddRange(letters);
            letters.Clear();
            if (token.Length > 0) words.Add(token);
        }
        return string.Join(' ', words);
    }

    public static string? EpisodeTitle(string fileName, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var trimmed = title.Trim();
        if (fileName.Contains(' ') || trimmed.Contains(' ') || !Separator().IsMatch(trimmed)) return trimmed;

        var tokens = Separator().Replace(trimmed, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tag = Array.FindIndex(tokens, token => ReleaseTag().IsMatch(token));
        var cleaned = JoinDotted(tag >= 0 ? tokens[..tag] : tokens);
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static int? LastYearBefore(string[] tokens, int end)
    {
        for (var i = end - 1; i >= 1; i--)
        {
            if (YearOf(tokens[i]) != null) return i;
        }
        return null;
    }

    private static int? YearOf(string token)
    {
        var match = YearToken().Match(token);
        if (!match.Success) return null;

        var year = int.Parse(match.Groups["Year"].Value, CultureInfo.InvariantCulture);
        return year <= DateTime.UtcNow.Year + 1 ? year : null;
    }

    [GeneratedRegex(@"[._]")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^[(\[]?(?<Year>(?:19|20)\d{2})[)\]]?$")]
    private static partial Regex YearToken();

    [GeneratedRegex(@"^(?:\d{3,4}[pi]|4k|bluray|blu-ray|bdrip|brrip|webrip|web-dl|webdl|hdtv|dvdrip|remux|x26[45]|h26[45]|hevc|s\d{1,4}(?:e\d{1,4})*)(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseTag();
}
