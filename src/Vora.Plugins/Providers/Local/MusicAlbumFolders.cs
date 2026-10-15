using System.Text.RegularExpressions;

namespace Vora.Plugins.Providers.Local;

public static partial class MusicAlbumFolders
{
    public static string AlbumFolder(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return DiscFolder().IsMatch(Path.GetFileName(folder)) ? Path.GetDirectoryName(folder) ?? folder : folder;
    }

    public static IReadOnlyList<IReadOnlyList<T>> Albums<T>(IReadOnlyList<T> files, Func<T, string> filePath, Func<T, int> discNumber)
    {
        var byFolder = files
            .GroupBy(file => AlbumFolder(filePath(file)), StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();
        if (byFolder.Count < 2) return [files];

        var discsByFolder = byFolder.Select(group => group.Select(discNumber).Distinct().ToList()).ToList();
        var eachFolderIsItsOwnDisc = discsByFolder.All(discs => discs.All(disc => disc > 0))
            && discsByFolder.SelectMany(discs => discs).Count() == discsByFolder.SelectMany(discs => discs).Distinct().Count();

        return eachFolderIsItsOwnDisc ? [files] : byFolder;
    }

    [GeneratedRegex(@"^(?:cd|disc|disk)[\s._-]*\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiscFolder();
}
