using System.Text.RegularExpressions;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Enums;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Media;

public static partial class ExtraNames
{
    public static void Apply(MediaDetailsVM details)
    {
        foreach (var extra in details.Extras)
        {
            extra.Title = Display(extra.Title, extra.ExtraType, details.Title);
        }
    }

    public static string Display(string stored, MediaExtraType type, string? parentTitle)
    {
        var name = stored.Trim();
        var generic = $"{parentTitle?.Trim()} {Label(type)}".Trim();
        if (name.Length == 0 || IsTheTitlesOwnName(name, parentTitle) || IsTheTypeAlone(name, type)) return generic;

        var readable = Readable(name);
        return readable.Length == 0 || IsTheTypeAlone(readable, type) ? generic : readable;
    }

    public static string Label(MediaExtraType type) => type switch
    {
        MediaExtraType.Trailer => "Trailer",
        MediaExtraType.Featurette => "Featurette",
        MediaExtraType.BehindTheScenes => "Behind the Scenes",
        MediaExtraType.DeletedScene => "Deleted Scene",
        MediaExtraType.Interview => "Interview",
        MediaExtraType.Scene => "Scene",
        MediaExtraType.Short => "Short",
        MediaExtraType.Clip => "Clip",
        MediaExtraType.Sample => "Sample",
        _ => "Extra"
    };

    private static bool IsTheTitlesOwnName(string name, string? parentTitle)
    {
        if (ReleaseNames.Read(name) is { } release)
        {
            return release.Year != null || Same(release.Title, parentTitle);
        }
        return TitleThenYear().IsMatch(name) || Same(name, parentTitle);
    }

    private static bool IsTheTypeAlone(string name, MediaExtraType type)
    {
        var bare = Letters(name);
        var word = Letters(Label(type));
        return bare == word || bare == word + "s" || bare == Letters(type.ToString());
    }

    private static string Readable(string name) =>
        ReleaseNames.Read(name)?.Title ?? ReleaseNames.EpisodeTitle(name, name) ?? name;

    private static bool Same(string? left, string? right) =>
        Letters(left) is { Length: > 0 } bare && bare == Letters(right);

    private static string Letters(string? text) =>
        new((text ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"^.+?[\s._-]+[(\[]?(?:19|20)\d{2}[)\]]?$")]
    private static partial Regex TitleThenYear();
}
