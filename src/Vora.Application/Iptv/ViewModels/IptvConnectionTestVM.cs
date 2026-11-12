namespace Vora.Application.Iptv.ViewModels;

public class IptvConnectionTestVM
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string? AccountStatus { get; set; }
    public DateTime? AccountExpiresAt { get; set; }
    public int? AccountMaxConnections { get; set; }
    public int ChannelCount { get; set; }
}
