namespace Vora.Domain.Entities.YouTube;

public class YouTubeAccountSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public YouTubeAccessSetting YouTubeAccess { get; set; } = YouTubeAccessSetting.Inherit;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
