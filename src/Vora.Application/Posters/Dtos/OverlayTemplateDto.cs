namespace Vora.Application.Posters.Dtos;

public class OverlayTemplateDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetMediaType { get; set; } = string.Empty;
    public Guid? TargetLibraryId { get; set; }
    public string ConfigurationJson { get; set; } = "[]";
}
