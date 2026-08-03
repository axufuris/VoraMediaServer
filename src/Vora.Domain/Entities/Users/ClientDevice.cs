namespace Vora.Domain.Entities.Users;

public class ClientDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DeviceId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string LastIpAddress { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    public bool IsBlocked { get; set; }

    public int MaxAudioChannels { get; set; } = 2;
    public List<string> SupportedVideoCodecs { get; set; } = new();
    public List<string> SupportedAudioCodecs { get; set; } = new();
    public List<string> SupportedContainers { get; set; } = new();

    public List<string>? SupportedHdrFormats { get; set; }
    public int MaxVideoBitDepth { get; set; }

    public Guid? LastUserId { get; set; }
    public Guid? LastProfileId { get; set; }
    public DateTime LastConnectedAt { get; set; } = DateTime.UtcNow;
}
