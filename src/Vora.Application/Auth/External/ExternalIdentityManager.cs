using Microsoft.Extensions.Logging;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Users;

namespace Vora.Application.Auth.External;

public enum UnlinkOutcome
{
    Unlinked,
    NotFound,
    LastWayIn
}

public interface IExternalIdentityManager
{
    Task<IReadOnlyList<LinkedIdentityVM>> ListAsync(Guid userId);
    Task<UnlinkOutcome> UnlinkAsync(Guid userId, Guid identityId);
}

public sealed class ExternalIdentityManager(
    IAuthProviderRepository providers,
    IUserRepository users,
    ISystemSettingsRepository settingsRepo,
    ILogger<ExternalIdentityManager> logger) : IExternalIdentityManager
{
    public async Task<IReadOnlyList<LinkedIdentityVM>> ListAsync(Guid userId) =>
        (await providers.GetIdentitiesForUserAsync(userId))
            .OrderBy(i => i.LinkedAt)
            .Select(i => new LinkedIdentityVM
            {
                Id = i.Id,
                ProviderId = i.ProviderId,
                ProviderName = i.Provider?.DisplayName ?? string.Empty,
                Email = i.Email,
                LinkedAt = i.LinkedAt,
                LastSignInAt = i.LastSignInAt
            })
            .ToList();

    public async Task<UnlinkOutcome> UnlinkAsync(Guid userId, Guid identityId)
    {
        var identities = await providers.GetIdentitiesForUserAsync(userId);
        var target = identities.FirstOrDefault(i => i.Id == identityId);
        if (target is null)
        {
            return UnlinkOutcome.NotFound;
        }

        var user = await users.GetUserByIdAsync(userId);
        if (user is null)
        {
            return UnlinkOutcome.NotFound;
        }

        var settings = await settingsRepo.GetSettingsAsync();
        var passwordWorks = user.HasPassword && (settings.PasswordSignInEnabled || user.IsAdmin);
        var otherWayIn = identities.Any(i => i.Id != identityId && i.Provider is { Enabled: true });
        if (!passwordWorks && !otherWayIn)
        {
            return UnlinkOutcome.LastWayIn;
        }

        await providers.DeleteIdentityAsync(identityId);
        logger.LogInformation("An account unlinked its {Provider} sign-in", target.Provider?.Slug);
        return UnlinkOutcome.Unlinked;
    }
}
