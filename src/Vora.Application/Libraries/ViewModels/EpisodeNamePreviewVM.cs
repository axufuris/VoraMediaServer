using Vora.Plugins.Dtos;

namespace Vora.Application.Libraries.ViewModels;

public enum EpisodeNameOutcome
{
    Name,
    Document,
    Guess,
    Unreadable
}

public class EpisodeNamePreviewFileVM
{
    public required string Path { get; set; }
    public EpisodeNameOutcome Outcome { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public int? EndEpisode { get; set; }
    public string? Title { get; set; }
}

public class EpisodeNamePreviewVM
{
    public string? PatternProblem { get; set; }
    public int FilesFound { get; set; }
    public bool MoreFilesThanChecked { get; set; }
    public int ReadFromName { get; set; }
    public int ReadFromDocument { get; set; }
    public int Guessed { get; set; }
    public int Unreadable { get; set; }
    public int? AutomaticUnreadable { get; set; }
    public int? AutomaticReadsDifferently { get; set; }
    public List<EpisodeNamePreviewFileVM> Files { get; set; } = new();

    public static EpisodeNamePreviewVM From(EpisodeNamePreviewDto preview) => new()
    {
        PatternProblem = preview.PatternProblem,
        FilesFound = preview.FilesFound,
        MoreFilesThanChecked = preview.MoreFilesThanChecked,
        ReadFromName = preview.ReadFromName,
        ReadFromDocument = preview.ReadFromDocument,
        Guessed = preview.Guessed,
        Unreadable = preview.Unreadable,
        AutomaticUnreadable = preview.AutomaticUnreadable,
        AutomaticReadsDifferently = preview.AutomaticReadsDifferently,
        Files = preview.Files.Select(file => new EpisodeNamePreviewFileVM
        {
            Path = file.Path,
            Outcome = file.Outcome switch
            {
                EpisodeNamePreviewOutcome.Name => EpisodeNameOutcome.Name,
                EpisodeNamePreviewOutcome.Document => EpisodeNameOutcome.Document,
                EpisodeNamePreviewOutcome.Guess => EpisodeNameOutcome.Guess,
                _ => EpisodeNameOutcome.Unreadable
            },
            Season = file.Season,
            Episode = file.Episode,
            EndEpisode = file.EndEpisode,
            Title = file.Title
        }).ToList()
    };
}
