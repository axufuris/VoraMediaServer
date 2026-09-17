namespace Vora.Application.FileSystem;

public static class SafePathResolver
{
    public static string? ResolveContainedFilePath(string rootDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..") || Path.IsPathRooted(fileName))
        {
            return null;
        }
        var rootFull = Path.GetFullPath(rootDirectory);
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        var candidateFull = Path.GetFullPath(Path.Combine(rootFull, fileName));
        return candidateFull.StartsWith(rootWithSep, StringComparison.Ordinal) ? candidateFull : null;
    }

    public static string? ResolveContainedSubPath(string rootDirectory, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || segments == null || segments.Length == 0)
        {
            return null;
        }
        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg) || seg.Contains('/') || seg.Contains('\\') || seg.Contains("..") || Path.IsPathRooted(seg))
            {
                return null;
            }
        }
        var rootFull = Path.GetFullPath(rootDirectory);
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        var combined = new string[segments.Length + 1];
        combined[0] = rootFull;
        Array.Copy(segments, 0, combined, 1, segments.Length);
        var candidateFull = Path.GetFullPath(Path.Combine(combined));
        return candidateFull.StartsWith(rootWithSep, StringComparison.Ordinal) ? candidateFull : null;
    }
}
