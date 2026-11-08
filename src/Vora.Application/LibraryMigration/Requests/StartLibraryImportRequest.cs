namespace Vora.Application.LibraryMigration.Requests;

public class StartLibraryImportRequest
{
    public string AccessToken { get; set; } = string.Empty;
    public string ServerClientIdentifier { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public bool IncludeWatchState { get; set; } = true;
    public bool IncludeRatings { get; set; } = true;
    public List<string> LibrarySectionKeys { get; set; } = new();
    public string? PlexUsername { get; set; }
    public bool SetAdminRatings { get; set; }
    public string? PlexAccountId { get; set; }
    public string? PlexAccountName { get; set; }
    public string? Pin { get; set; }
    public Guid? ProfileId { get; set; }
}
