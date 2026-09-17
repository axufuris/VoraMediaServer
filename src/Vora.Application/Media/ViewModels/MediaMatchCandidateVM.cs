namespace Vora.Application.Media.ViewModels;

public class MediaMatchCandidateVM
{
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public string ProviderName { get; set; } = string.Empty;
}
