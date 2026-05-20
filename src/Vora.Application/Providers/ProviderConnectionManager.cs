using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Providers.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Providers;

public interface IProviderConnectionManager
{
    Task LinkProviderAsync(Guid userId, string providerName, string accessToken, string? refreshToken, DateTime? expiresAt);
    Task UnlinkProviderAsync(Guid userId, string providerName);
    Task<IEnumerable<ProviderConnectionVM>> GetUserConnectionsAsync(Guid userId);
}

public class ProviderConnectionManager(
    IProviderConnectionRepository repository,
    IEnumerable<IMediaProvider> mediaProviders,
    ILogger<ProviderConnectionManager> logger) : IProviderConnectionManager
{
    public async Task LinkProviderAsync(Guid userId, string providerName, string accessToken, string? refreshToken, DateTime? expiresAt)
    {
        var provider = mediaProviders.FirstOrDefault(p => p.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
        {
            throw new InvalidOperationException($"Plugin for {providerName} is not installed.");
        }

        try
        {
            var existing = await repository.GetConnectionAsync(userId, providerName);

            if (existing != null)
            {
                existing.AccessToken = accessToken;
                existing.RefreshToken = refreshToken;
                existing.ExpiresAt = expiresAt;
                await repository.UpdateConnectionAsync(existing);
                return;
            }

            await repository.AddConnectionAsync(new UserProviderConnection
            {
                UserId = userId,
                ProviderName = providerName,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to link {ProviderName} for user {UserId}.", providerName, userId);
            throw;
        }
    }

    public async Task UnlinkProviderAsync(Guid userId, string providerName)
    {
        try
        {
            var connection = await repository.GetConnectionAsync(userId, providerName);
            if (connection != null)
            {
                await repository.RemoveConnectionAsync(connection);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unlink {ProviderName} for user {UserId}.", providerName, userId);
            throw;
        }
    }

    public Task<IEnumerable<ProviderConnectionVM>> GetUserConnectionsAsync(Guid userId) =>
        repository.GetProjectedUserConnectionsAsync(userId, ProviderConnectionVM.Projection);
}
