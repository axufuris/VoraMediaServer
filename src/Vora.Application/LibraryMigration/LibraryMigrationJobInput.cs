namespace Vora.Application.LibraryMigration;

public class LibraryMigrationJobInput
{
    public required string ProviderId { get; set; }
    public required string AccessToken { get; set; }
    public required Guid RequestedByAccountId { get; set; }
    public required string ServerClientIdentifier { get; set; }
    public required string ServerName { get; set; }
    public required bool IncludeWatchState { get; set; }
    public required bool IncludeRatings { get; set; }
    public required IReadOnlyList<string> LibrarySectionKeys { get; set; }
    public required List<LibraryMigrationMappingInput> Mappings { get; set; }
    public bool SetAdminRatings { get; set; }
}

public class LibraryMigrationMappingInput
{
    public const string SignedInAccount = "self";

    public required string AccountId { get; set; }
    public required string AccountName { get; set; }
    public required Guid ProfileId { get; set; }
    public required string ProfileName { get; set; }
    public string? Pin { get; set; }
}
