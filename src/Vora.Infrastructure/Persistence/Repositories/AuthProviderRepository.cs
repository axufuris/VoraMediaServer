using Microsoft.EntityFrameworkCore;
using Vora.Application.Auth.External;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class AuthProviderRepository(VoraDbContext context) : IAuthProviderRepository
{
    public Task<List<AuthProvider>> GetAllAsync() =>
        context.AuthProviders.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName).ToListAsync();

    public Task<List<AuthProvider>> GetEnabledAsync() =>
        context.AuthProviders.AsNoTracking().Where(p => p.Enabled).OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName).ToListAsync();

    public Task<AuthProvider?> GetByIdAsync(Guid id) =>
        context.AuthProviders.FirstOrDefaultAsync(p => p.Id == id);

    public Task<AuthProvider?> GetBySlugAsync(string slug) =>
        context.AuthProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug);

    public async Task AddAsync(AuthProvider provider)
    {
        await context.AuthProviders.AddAsync(provider);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(AuthProvider provider)
    {
        context.AuthProviders.Update(provider);
        await context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var provider = await context.AuthProviders.FirstOrDefaultAsync(p => p.Id == id);
        if (provider is null) return false;
        context.AuthProviders.Remove(provider);
        await context.SaveChangesAsync();
        return true;
    }

    public Task<int> CountUsersWithoutOtherWayInAsync(Guid providerId) =>
        context.UserExternalIdentities
            .Where(i => i.ProviderId == providerId
                && context.Users.Any(u => u.Id == i.UserId && !u.HasPassword)
                && !context.UserExternalIdentities.Any(other => other.UserId == i.UserId && other.ProviderId != providerId))
            .Select(i => i.UserId)
            .Distinct()
            .CountAsync();

    public Task<UserExternalIdentity?> GetIdentityAsync(Guid providerId, string subject) =>
        context.UserExternalIdentities.FirstOrDefaultAsync(i => i.ProviderId == providerId && i.Subject == subject);

    public Task<UserExternalIdentity?> GetIdentityByIdAsync(Guid id) =>
        context.UserExternalIdentities.FirstOrDefaultAsync(i => i.Id == id);

    public Task<List<UserExternalIdentity>> GetIdentitiesForUserAsync(Guid userId) =>
        context.UserExternalIdentities.AsNoTracking().Include(i => i.Provider).Where(i => i.UserId == userId).ToListAsync();

    public Task<int> CountIdentitiesForUserAsync(Guid userId) =>
        context.UserExternalIdentities.CountAsync(i => i.UserId == userId);

    public async Task AddIdentityAsync(UserExternalIdentity identity)
    {
        await context.UserExternalIdentities.AddAsync(identity);
        await context.SaveChangesAsync();
    }

    public async Task UpdateIdentityAsync(UserExternalIdentity identity)
    {
        context.UserExternalIdentities.Update(identity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteIdentityAsync(Guid id)
    {
        var identity = await context.UserExternalIdentities.FirstOrDefaultAsync(i => i.Id == id);
        if (identity is null) return;
        context.UserExternalIdentities.Remove(identity);
        await context.SaveChangesAsync();
    }
}
