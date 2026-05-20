namespace Vora.Domain.Entities.Templates;

public class ClientTemplateSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TemplateId { get; set; }
    public required string Name { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
