namespace Vora.Plugins.Dtos;

public class TheaterDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<ShowtimeDto> Showtimes { get; set; } = new();
}
