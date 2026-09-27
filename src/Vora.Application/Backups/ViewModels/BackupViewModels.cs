namespace Vora.Application.Backups.ViewModels;

public sealed class BackupSummaryVM
{
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long FileSizeBytes { get; set; }
    public int SectionCount { get; set; }
    public string Reason { get; set; } = "manual";
    public string? VoraServerVersion { get; set; }
    public bool ManifestReadable { get; set; }
}

public sealed class BackupSectionVM
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool RequiresExplicitConfirm { get; set; }
    public string? DestructiveWarning { get; set; }
    public long SizeBytes { get; set; }
    public int? ItemCount { get; set; }
}

public sealed class BackupManifestVM
{
    public string FileName { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string VoraServerVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public List<BackupSectionVM> Sections { get; set; } = new();
}

public sealed class BackupSettingsVM
{
    public bool AutoBackupEnabled { get; set; }
    public string Cadence { get; set; } = "Daily";
    public int Hour { get; set; }
    public int Minute { get; set; }
    public string DayOfWeek { get; set; } = "Sunday";
    public int DayOfMonth { get; set; } = 1;
    public int MaxToKeep { get; set; } = 10;
    public string? OverrideDirectory { get; set; }
    public string EffectiveDirectory { get; set; } = string.Empty;
    public DateTime? LastSuccessfulRunUtc { get; set; }
    public DateTime? NextScheduledRunUtc { get; set; }
    public List<string>? IncludedSectionKeys { get; set; }
    public List<AvailableSectionVM> AvailableSections { get; set; } = new();
}

public sealed class CreateBackupRequest
{
    public string Reason { get; set; } = "manual";
}

public sealed class RestoreBackupRequest
{
    public List<string> SectionKeys { get; set; } = new();
    public bool AcknowledgeAdminLoss { get; set; }
}

public sealed class RestoreBackupResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<RestoreSectionResult> Sections { get; set; } = new();
}

public sealed class RestoreSectionResult
{
    public string Key { get; set; } = string.Empty;
    public bool Restored { get; set; }
    public int RowsImported { get; set; }
    public int RowsSkipped { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class AvailableSectionVM
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool RequiresExplicitConfirm { get; set; }
    public string? DestructiveWarning { get; set; }
}
