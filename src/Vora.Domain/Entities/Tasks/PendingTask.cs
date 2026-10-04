namespace Vora.Domain.Entities.Tasks;

public class PendingTask
{
    public Guid Id { get; set; }
    public long Sequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string Name { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; }
}
