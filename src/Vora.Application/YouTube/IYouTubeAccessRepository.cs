using Vora.Domain.Entities.Users;
using Vora.Domain.Entities.YouTube;

namespace Vora.Application.YouTube;

public interface IYouTubeAccessRepository
{
    Task<UserProfile?> GetProfileWithUserAsync(Guid profileId);
    Task<YouTubeAccountSettings?> GetAccountSettingsAsync(Guid accountId);
    Task<YouTubeProfileSettings?> GetProfileSettingsAsync(Guid profileId);
    Task UpsertAccountSettingsAsync(YouTubeAccountSettings settings);
    Task UpsertProfileSettingsAsync(YouTubeProfileSettings settings);
}
