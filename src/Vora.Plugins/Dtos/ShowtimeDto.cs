namespace Vora.Plugins.Dtos;

public class ShowtimeDto
{
    public string Time { get; set; } = string.Empty;
    public string Format { get; set; } = "Standard";
    public string? TicketUrl { get; set; }
}
