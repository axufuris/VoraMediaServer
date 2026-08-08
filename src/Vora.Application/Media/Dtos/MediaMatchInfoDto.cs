namespace Vora.Application.Media.Dtos;

public class MediaMatchInfoDto
{
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = "Movie";
}
