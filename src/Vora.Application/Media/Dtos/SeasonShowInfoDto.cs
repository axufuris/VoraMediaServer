namespace Vora.Application.Media.Dtos;

public class SeasonShowInfoDto
{
    public Guid SeasonId { get; set; }
    public string ShowTitle { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
}
