namespace Vora.Domain.Entities.Users;

public class ProfileDeviceSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DeviceId { get; set; } = string.Empty;

    public string NavPrefsJson { get; set; } = string.Empty;
    public string PlaybackPrefs { get; set; } = string.Empty;
    public string IptvPrefsJson { get; set; } = string.Empty;
    public string RadioPrefsJson { get; set; } = string.Empty;
    public string DiscoveryLayoutJson { get; set; } = string.Empty;
    public string HomeLayoutJson { get; set; } = string.Empty;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;
}
