namespace Vora.Application.Media.Dtos;

public class SilenceDetectionInputsDto
{
    public List<string> FilePaths { get; set; } = new();
    public TimeSpan? Duration { get; set; }
    public bool HasMidCreditsStinger { get; set; }
    public bool HasPostCreditsStinger { get; set; }
}
