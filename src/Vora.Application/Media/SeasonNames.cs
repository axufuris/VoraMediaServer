using System.Text.RegularExpressions;

namespace Vora.Application.Media;

public static partial class SeasonNames
{
    public const string Specials = "Specials";

    public static string Default(int seasonNumber) =>
        EpisodeSequence.IsSpecial(seasonNumber) ? Specials : $"Season {seasonNumber}";

    public static string Resolve(int seasonNumber, string? providerName)
    {
        var name = providerName?.Trim();
        if (string.IsNullOrEmpty(name)) return Default(seasonNumber);
        return EpisodeSequence.IsSpecial(seasonNumber) && NumberedSeasonZero().IsMatch(name) ? Specials : name;
    }

    [GeneratedRegex(@"^season\s*0+$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedSeasonZero();
}
