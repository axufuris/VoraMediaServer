namespace Vora.Application.Iptv.ViewModels;

public class IptvEpgSourceVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string XmlTvUrl { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
