namespace Vora.Plugins.Dtos;

public class RemoteSyncScopeDto
{
    public bool IncludeWatchState { get; set; } = true;
    public bool IncludeRatings { get; set; } = true;
    public required IReadOnlyList<string> LibrarySectionKeys { get; set; }
}
