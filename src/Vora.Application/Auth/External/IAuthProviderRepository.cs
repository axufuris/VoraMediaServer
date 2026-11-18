using Vora.Domain.Entities.Users;

namespace Vora.Application.Auth.External;

public interface IAuthProviderRepository
{
    Task<List<AuthProvider>> GetAllAsync();
    Task<List<AuthProvider>> GetEnabledAsync();
    Task<AuthProvider?> GetByIdAsync(Guid id);
    Task<AuthProvider?> GetBySlugAsync(string slug);
    Task AddAsync(AuthProvider provider);
    Task UpdateAsync(AuthProvider provider);
    Task<bool> DeleteAsync(Guid id);
    Task<int> CountUsersWithoutOtherWayInAsync(Guid providerId);

    Task<UserExternalIdentity?> GetIdentityAsync(Guid providerId, string subject);
    Task<UserExternalIdentity?> GetIdentityByIdAsync(Guid id);
    Task<List<UserExternalIdentity>> GetIdentitiesForUserAsync(Guid userId);
    Task<int> CountIdentitiesForUserAsync(Guid userId);
    Task AddIdentityAsync(UserExternalIdentity identity);
    Task UpdateIdentityAsync(UserExternalIdentity identity);
    Task DeleteIdentityAsync(Guid id);
}
