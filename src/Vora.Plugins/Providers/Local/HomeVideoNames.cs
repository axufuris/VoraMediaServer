using System.Globalization;
using System.Text.RegularExpressions;

namespace Vora.Plugins.Providers.Local;

public static partial class HomeVideoNames
{
    public static string Title(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath).Trim();
        return name.Length == 0 ? Path.GetFileName(filePath) : name;
    }

    public static DateOnly? Date(string filePath) => DateInName(Path.GetFileNameWithoutExtension(filePath)) ?? FileDate(filePath);

    public static DateOnly? DateInName(string name)
    {
        foreach (Match match in NamedDate().Matches(name))
        {
            var year = int.Parse(match.Groups["Year"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["Month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["Day"].Value, CultureInfo.InvariantCulture);
            if (day <= DateTime.DaysInMonth(year, month)) return new DateOnly(year, month, day);
        }
        return null;
    }

    public static string? Resolution(string filePath)
    {
        var match = NamedResolution().Match(Path.GetFileNameWithoutExtension(filePath));
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static DateOnly? FileDate(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? DateOnly.FromDateTime(File.GetLastWriteTimeUtc(filePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<!\d)(?<Year>(19|20)\d{2})(?<Separator>[-._ ]?)(?<Month>0[1-9]|1[0-2])\k<Separator>(?<Day>0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NamedDate();

    [GeneratedRegex(@"480p|720p|1080p|4k|2160p", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedResolution();
}
