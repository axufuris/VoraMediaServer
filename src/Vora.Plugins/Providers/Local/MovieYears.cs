namespace Vora.Plugins.Providers.Local;

public static class MovieYears
{
    public static int? In(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (LocalIdentity.YearIn(name) is int named) return named;

        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (LocalIdentity.YearIn(folderName) is int folder) return folder;

        if (ReleaseNames.Read(name) is { Year: int release }) return release;
        return ReleaseNames.Read(folderName) is { Year: int releaseFolder } ? releaseFolder : null;
    }

    public static List<int> Named(IEnumerable<string> filePaths)
    {
        var years = filePaths
            .Select(In)
            .OfType<int>()
            .Distinct()
            .Order()
            .ToList();
        return Clusters(years).Count > 1 ? years : [];
    }

    public static List<List<int>> Clusters(IEnumerable<int> years)
    {
        var clusters = new List<List<int>>();
        foreach (var year in years.Distinct().Order())
        {
            if (clusters.Count > 0 && year - clusters[^1][0] <= LocalIdentity.YearTolerance) clusters[^1].Add(year);
            else clusters.Add([year]);
        }
        return clusters;
    }

    public static List<string> FilesOfOtherYears(IReadOnlyList<(string FilePath, int? Year)> files, int? movieYear)
    {
        var dated = files.Where(f => f.Year != null).ToList();
        var clusters = Clusters(dated.Select(f => f.Year!.Value));
        if (clusters.Count < 2) return [];

        var sizes = clusters.ToDictionary(c => c[0], c => dated.Count(f => c.Contains(f.Year!.Value)));
        var keep = movieYear is int year ? clusters.FirstOrDefault(c => c.Any(y => Math.Abs(y - year) <= LocalIdentity.YearTolerance)) : null;
        keep ??= clusters.OrderByDescending(c => sizes[c[0]]).ThenBy(c => c[0]).First();

        return dated
            .Where(f => !keep.Contains(f.Year!.Value))
            .Select(f => f.FilePath)
            .ToList();
    }
}
