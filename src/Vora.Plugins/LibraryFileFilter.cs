namespace Vora.Plugins;

// The single rule for which files in a library folder are left alone. The
// scanner and the folder watcher both read it. They used to keep their own
// copies, and the watcher's only checked the file name and never saw the
// server-wide ignored folders. So every song in an excluded folder, or in the
// .recycle bin, was skipped by the scan but looked new to the watcher's startup
// check, and once there were more than a few dozen, every restart queued a full
// library scan that skipped them again.
public static class LibraryFileFilter
{
    // A library's own filters plus the server-wide ignored folders.
    public static List<string> Combine(IEnumerable<string>? libraryFilters, IEnumerable<string>? ignoredFolders) =>
        (libraryFilters ?? Enumerable.Empty<string>())
            .Concat(ignoredFolders ?? Enumerable.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Excluded when the file name contains a filter (".TDARR" for a copy still
    // transcoding), when any folder on its path is one (".recycle"), or when it
    // is a macOS resource fork.
    public static bool IsExcluded(string filePath, IReadOnlyList<string>? excludeFilters)
    {
        if (IsMacResourceFork(filePath)) return true;
        if (excludeFilters == null || excludeFilters.Count == 0) return false;

        var name = Path.GetFileName(filePath);
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var segments = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var raw in excludeFilters)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var filter = raw.Trim();
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            if (segments.Any(s => s.Equals(filter, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    // Finder writes "._Song.mp3" beside "Song.mp3" on shares that don't store
    // extended attributes. It carries the media extension but is a few-kilobyte
    // metadata stub, so the music scanner's tag read fails on it every time.
    public static bool IsMacResourceFork(string filePath) =>
        Path.GetFileName(filePath).StartsWith("._", StringComparison.Ordinal);
}
