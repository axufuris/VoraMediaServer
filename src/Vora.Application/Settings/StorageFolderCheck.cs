namespace Vora.Application.Settings;

public sealed record StorageFolderProblem(string Setting, string Folder, string Reason);

public static class StorageFolderCheck
{
    public static IReadOnlyList<StorageFolderProblem> FindUnwritable(IEnumerable<(string Setting, string Folder)> folders)
    {
        var problems = new List<StorageFolderProblem>();

        foreach (var (setting, folder) in folders)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".vora-write-check-" + Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(new StorageFolderProblem(setting, folder, ex.Message));
            }
        }

        return problems;
    }
}
