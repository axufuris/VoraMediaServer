namespace Vora.Application.LibraryMigration;

public class LibraryMigrationJobInput
{
    public required string ProviderId { get; set; }
    public required string AdminAccessToken { get; set; }
    public required string ServerClientIdentifier { get; set; }
    public required string ServerName { get; set; }
    public required string ConnectionUri { get; set; }
    public required bool IncludeWatchState { get; set; }
    public required bool IncludeRatings { get; set; }
    public required IReadOnlyList<string> LibrarySectionKeys { get; set; }
    public required List<LibraryMigrationMappingInput> Mappings { get; set; }

    public bool SelfService { get; set; }
}

public class LibraryMigrationMappingInput
{
    public required string AccountId { get; set; }
    public required string AccountName { get; set; }
    public required Guid ProfileId { get; set; }
    public required string ProfileName { get; set; }
    public string? Pin { get; set; }
}
