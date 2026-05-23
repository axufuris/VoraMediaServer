using Vora.Domain.Entities.YouTube;

namespace Vora.Application.YouTube.ViewModels;

public class YouTubeAccountSettingsVM
{
    public Guid AccountId { get; set; }
    public YouTubeAccessSetting YouTubeAccess { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
