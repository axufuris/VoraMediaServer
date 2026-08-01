namespace Vora.Application.LibraryMigration.ViewModels;

public enum LibraryMigrationUserState
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public class LibraryMigrationUserStatusVM
{
    public required string AccountId { get; set; }
    public required string AccountName { get; set; }
    public required Guid ProfileId { get; set; }
    public required string ProfileName { get; set; }
    public required LibraryMigrationUserState State { get; set; }
    public int WatchStatesFetched { get; set; }
    public int WatchStatesImported { get; set; }
    public int RatingsFetched { get; set; }
    public int RatingsImported { get; set; }
    public int Skipped { get; set; }
    public List<string> SkippedSamples { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
