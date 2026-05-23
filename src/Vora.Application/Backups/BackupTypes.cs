namespace Vora.Application.Backups;

public enum BackupSectionGroup
{
    Settings,
    Templates,
    Library,
    Iptv,
    Discovery,
    Security,
    UserData
}

public enum BackupCadence
{
    Off,
    Daily,
    Weekly,
    Monthly
}

public sealed class BackupSectionImportResult
{
    public int RowsImported { get; set; }
    public int RowsSkipped { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class BackupSettings
{
    public bool AutoBackupEnabled { get; set; }
    public BackupCadence Cadence { get; set; } = BackupCadence.Daily;
    public int Hour { get; set; } = 3;
    public int Minute { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;
    public int DayOfMonth { get; set; } = 1;
    public int MaxToKeep { get; set; } = 10;
    public string? OverrideDirectory { get; set; }
    public DateTime? LastSuccessfulRunUtc { get; set; }
    public List<string>? IncludedSectionKeys { get; set; }
}
