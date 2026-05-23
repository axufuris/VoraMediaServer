using Vora.Domain.Entities.YouTube;

namespace Vora.Application.YouTube.Requests;

public class UpdateYouTubeAccountSettingsRequest
{
    public YouTubeAccessSetting YouTubeAccess { get; set; } = YouTubeAccessSetting.Inherit;
}
