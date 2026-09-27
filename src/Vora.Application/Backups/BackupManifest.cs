namespace Vora.Application.Backups;

public sealed class BackupManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string VoraServerVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Kind { get; set; } = "configuration";
    public string Reason { get; set; } = "manual";
    public List<BackupSectionManifestEntry> Sections { get; set; } = new();
    public long TotalSizeBytes { get; set; }
}

public sealed class BackupSectionManifestEntry
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool RequiresExplicitConfirm { get; set; }
    public string? DestructiveWarning { get; set; }
    public long SizeBytes { get; set; }
    public int? ItemCount { get; set; }
}
