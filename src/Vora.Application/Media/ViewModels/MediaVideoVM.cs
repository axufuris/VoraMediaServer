namespace Vora.Application.Media;

public class MediaVideoVM
{
    public string VideoKey { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Site { get; set; }
    public string? Type { get; set; }
    public bool IsOfficial { get; set; }
}
