using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Local;

public static partial class LocalMediaAssets
{
    public const string ShowDocument = "tvshow.nfo";
    public const string SeasonDocument = "season.nfo";
    public const string MovieDocument = "movie.nfo";
    public const string LocalLanguage = "Local";

    private const string EpisodeType = "Episode";
    private const string SeasonType = "Season";
    private const string ShowType = "TvShow";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly string[] PosterNames = ["poster", "folder", "cover"];
    private static readonly string[] BackdropNames = ["fanart", "backdrop", "background"];
    private static readonly string[] LogoNames = ["logo", "clearlogo"];
    private static readonly string[] OwnSuffixes = ["-poster", "-fanart", "-logo", "-thumb", string.Empty];

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static string? DirectoryPath(string path) => Directory.Exists(path) ? path : Path.GetDirectoryName(path);

    public static string? ShowDirectory(string episodePath, LocalMediaFolders folders)
    {
        var directory = DirectoryPath(episodePath);
        if (directory == null) return null;
        if (NamedFile(folders.Files(directory), ShowDocument) != null) return directory;

        var name = Path.GetFileName(directory);
        var parent = Path.GetDirectoryName(directory);
        if (parent == null) return directory;
        if (IsSeasonFolderName(name)) return parent;

        return StartsLikeASeasonFolder(name) && NamedFile(folders.Files(parent), ShowDocument) != null
            ? parent
            : directory;
    }

    public static bool IsSeasonFolderName(string name) => SeasonFolder().IsMatch(name);

    public static string? NamedFile(IEnumerable<string> files, string name) =>
        files.FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string? Image(IEnumerable<string> files, params string[] names) =>
        names.Select(name => files.FirstOrDefault(file => IsImage(file)
            && Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(file => file != null);

    public static string? SeasonPoster(string directory, int seasonNumber, LocalMediaFolders folders)
    {
        var files = folders.Files(directory);
        var seasonName = SeasonName(seasonNumber);
        var poster = Image(files, seasonName + "-poster", seasonName);
        if (poster != null) return poster;

        var showDirectory = ShowDirectory(directory, folders);
        if (showDirectory == null || showDirectory == directory) return null;
        return Image(files, PosterNames)
            ?? Image(folders.Files(showDirectory), seasonName + "-poster", seasonName);
    }

    public static IReadOnlyList<ArtworkResult> FindArtwork(string mediaType, string path, int? seasonNumber, LocalMediaFolders folders)
    {
        var results = new List<ArtworkResult>();
        var isDirectory = Directory.Exists(path);
        var directory = isDirectory ? path : Path.GetDirectoryName(path);
        var files = folders.Files(directory);
        var baseName = isDirectory ? null : Path.GetFileNameWithoutExtension(path);

        if (!isDirectory && mediaType is SeasonType or ShowType) return results;

        if (mediaType == EpisodeType)
        {
            if (baseName != null) Add(ArtworkKind.Backdrop, Image(files, baseName + "-thumb", baseName));
            return results;
        }

        if (mediaType == SeasonType)
        {
            if (directory == null || seasonNumber is not int season) return results;

            var showDirectory = ShowDirectory(directory, folders);
            var backdropName = SeasonName(season) + "-fanart";
            Add(ArtworkKind.Poster, SeasonPoster(directory, season, folders));
            Add(ArtworkKind.Backdrop, Image(files, backdropName) ?? Image(folders.Files(showDirectory), backdropName));
            return results;
        }

        var sharedNamesApply = baseName == null || HoldsOneFilm(files, path);
        Add(ArtworkKind.Poster, Own("-poster") ?? Shared(PosterNames));
        Add(ArtworkKind.Backdrop, Own("-fanart") ?? Shared(BackdropNames));
        Add(ArtworkKind.Logo, Own("-logo") ?? Shared(LogoNames));
        return results;

        string? Own(string suffix) => baseName == null ? null : Image(files, baseName + suffix);

        string? Shared(string[] names) => sharedNamesApply ? Image(files, names) : null;

        void Add(ArtworkKind kind, string? image)
        {
            if (image != null) results.Add(new ArtworkResult { Kind = kind, Url = image, Language = LocalLanguage });
        }
    }

    public static string SeasonName(int seasonNumber) =>
        seasonNumber == 0 ? "season-specials" : $"season{seasonNumber:D2}";

    public static bool HoldsOneFilm(IEnumerable<string> files, string videoPath)
    {
        var own = FilmKey(Path.GetFileNameWithoutExtension(videoPath));
        return files
            .Where(MediaFileExtensions.IsVideo)
            .Where(file => !VoraLocalMediaScannerProvider.IsExtraFile(file))
            .All(file => FilmKey(Path.GetFileNameWithoutExtension(file)) == own);
    }

    public static string FilmKey(string fileName)
    {
        var year = FirstYear().Match(fileName);
        var title = year.Success
            ? fileName[..year.Index] + year.Groups["Year"].Value
            : VersionMarker().Split(fileName, 2)[0];
        return new string(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    public static bool IsArtworkName(string fileName, int? seasonNumber, IEnumerable<string> mediaFileNames)
    {
        if (!IsImage(fileName)) return false;

        var name = Path.GetFileNameWithoutExtension(fileName);
        if (PosterNames.Concat(BackdropNames).Concat(LogoNames).Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        if (seasonNumber is int season)
        {
            var seasonName = SeasonName(season);
            if (name.Equals(seasonName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(seasonName + "-poster", StringComparison.OrdinalIgnoreCase)
                || name.Equals(seasonName + "-fanart", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return mediaFileNames
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Any(baseName => OwnSuffixes.Any(suffix => name.Equals(baseName + suffix, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool StartsLikeASeasonFolder(string name) =>
        name.StartsWith("Season", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Specials", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(season|series)[\s._-]*\d+\b|^specials?$|^s\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"(?<![0-9])(?<Year>(19|20)\d{2})(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex FirstYear();

    [GeneratedRegex(@"\s-\s|\s*[\{\[]", RegexOptions.CultureInvariant)]
    private static partial Regex VersionMarker();
}
