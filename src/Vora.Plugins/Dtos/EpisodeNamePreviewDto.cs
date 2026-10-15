namespace Vora.Plugins.Dtos;

public enum EpisodeNamePreviewOutcome
{
    Name,
    Document,
    Guess,
    Unreadable
}

public sealed record EpisodeNamePreviewFileDto(string Path, EpisodeNamePreviewOutcome Outcome, int? Season, int? Episode, int? EndEpisode, string? Title);

public sealed class EpisodeNamePreviewDto
{
    public string? PatternProblem { get; init; }
    public int FilesFound { get; init; }
    public bool MoreFilesThanChecked { get; init; }
    public int ReadFromName { get; init; }
    public int ReadFromDocument { get; init; }
    public int Guessed { get; init; }
    public int Unreadable { get; init; }
    public int? AutomaticUnreadable { get; init; }
    public int? AutomaticReadsDifferently { get; init; }
    public required IReadOnlyList<EpisodeNamePreviewFileDto> Files { get; init; }
}
