namespace Vora.Domain.Entities.Posters;

public class OverlayTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public required string TargetMediaType { get; set; }
    public Guid? TargetLibraryId { get; set; }

    public string ConfigurationJson { get; set; } = "[]";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
