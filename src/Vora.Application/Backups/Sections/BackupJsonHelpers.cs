namespace Vora.Application.Backups.Sections;

internal static class BackupJsonHelpers
{
    public static string RowsFileName => "rows.json";

    public static string EntityFile(string sectionKey, string fileName) => $"{sectionKey}/{fileName}";
}
