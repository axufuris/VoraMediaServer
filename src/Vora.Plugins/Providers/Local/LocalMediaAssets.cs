using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Vora.Plugins.Providers.Local;

public static partial class LocalMediaAssets
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public static string? DirectoryPath(string path) => Directory.Exists(path) ? path : Path.GetDirectoryName(path);

    public static string? ShowDirectory(string episodePath)
    {
        var directory = DirectoryPath(episodePath);
        return directory != null && SeasonFolder().IsMatch(Path.GetFileName(directory))
            ? Path.GetDirectoryName(directory)
            : directory;
    }

    public static string[] Files(string? directory, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        try
        {
            return Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not read local assets in {Directory}.", directory);
            return [];
        }
    }

    public static string? NamedFile(IEnumerable<string> files, string name) =>
        files.FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string? Image(IEnumerable<string> files, params string[] names) =>
        names.Select(name => files.FirstOrDefault(file => ImageExtensions.Contains(Path.GetExtension(file))
            && Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(file => file != null);

    [GeneratedRegex(@"^(season|series|specials?)\b|^s\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolder();
}
