namespace Vora.Application.Media.Dtos;

public class FingerprintInputDto
{
    public Guid MediaItemId { get; set; }
    public string? FilePath { get; set; }
    public TimeSpan? Duration { get; set; }
}

public class StoredAudioFingerprintDto
{
    public byte[]? HeadFingerprint { get; set; }
    public double HeadPointDurationSeconds { get; set; }
    public string FileIdentity { get; set; } = "";
}
