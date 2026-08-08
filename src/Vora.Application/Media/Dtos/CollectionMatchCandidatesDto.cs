namespace Vora.Application.Media.Dtos;

public class CollectionMatchCandidatesDto
{
    public List<MediaTitleCandidateDto> Movies { get; set; } = new();
    public List<MediaTitleCandidateDto> Shows { get; set; } = new();
    public List<SeasonMatchCandidateDto> Seasons { get; set; } = new();
}

public class MediaTitleCandidateDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
}

public class SeasonMatchCandidateDto
{
    public Guid Id { get; set; }
    public Guid TvShowId { get; set; }
    public int SeasonNumber { get; set; }
}
