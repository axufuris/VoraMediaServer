namespace Vora.Application.LibraryMigration.ViewModels;

public enum LibraryMigrationJobState
{
    Pending,
    Running,
    Completed,
    Failed
}

public class LibraryMigrationJobVM
{
    public required Guid JobId { get; set; }
    public required string ProviderId { get; set; }
    public required string ServerName { get; set; }
    public required LibraryMigrationJobState State { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public required List<LibraryMigrationUserStatusVM> Users { get; set; }
}
