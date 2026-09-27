namespace Vora.Domain.Entities.Iptv;

public class IptvEpgSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string XmlTvUrl { get; set; }

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? LastError { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
