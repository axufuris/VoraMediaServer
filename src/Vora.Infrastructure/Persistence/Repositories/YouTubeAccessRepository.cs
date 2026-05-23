using Microsoft.EntityFrameworkCore;
using Vora.Application.YouTube;
using Vora.Domain.Entities.Users;
using Vora.Domain.Entities.YouTube;

namespace Vora.Infrastructure.Persistence.Repositories;

public class YouTubeAccessRepository(VoraDbContext context) : IYouTubeAccessRepository
{
    public Task<UserProfile?> GetProfileWithUserAsync(Guid profileId) =>
        context.UserProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == profileId);

    public Task<YouTubeAccountSettings?> GetAccountSettingsAsync(Guid accountId) =>
        context.YouTubeAccountSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccountId == accountId);

    public Task<YouTubeProfileSettings?> GetProfileSettingsAsync(Guid profileId) =>
        context.YouTubeProfileSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserProfileId == profileId);

    public async Task UpsertAccountSettingsAsync(YouTubeAccountSettings settings)
    {
        var existing = await context.YouTubeAccountSettings.FirstOrDefaultAsync(s => s.AccountId == settings.AccountId);
        if (existing is null)
        {
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await context.YouTubeAccountSettings.AddAsync(settings);
        }
        else
        {
            existing.YouTubeAccess = settings.YouTubeAccess;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync();
    }

    public async Task UpsertProfileSettingsAsync(YouTubeProfileSettings settings)
    {
        var existing = await context.YouTubeProfileSettings.FirstOrDefaultAsync(s => s.UserProfileId == settings.UserProfileId);
        if (existing is null)
        {
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await context.YouTubeProfileSettings.AddAsync(settings);
        }
        else
        {
            existing.IsEnabled = settings.IsEnabled;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync();
    }
}
