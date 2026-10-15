using System.Text;

namespace Vora.Plugins.Providers.Local;

public static class TrackTitles
{
    private const string Apostrophes = "'’‘`´";

    public static bool Same(string? left, string? right) =>
        Normalize(left) is { Length: > 0 } a && a == Normalize(right);

    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var builder = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (Apostrophes.Contains(c)) continue;
            else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
        }
        return builder.ToString().TrimEnd();
    }
}
