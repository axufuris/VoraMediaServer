using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Users;

namespace Vora.Application.Auth;

public interface IJwtSecurityStampValidator
{
    Task<bool> IsStampValidAsync(Guid userId, string stamp, Guid? profileId, string? profileStamp);
}

public class JwtSecurityStampValidator(IUserRepository repository, IMemoryCache cache) : IJwtSecurityStampValidator
{
    private const string UserStampCachePrefix = "auth:stamp:user:";
    private const string ProfileStampCachePrefix = "auth:stamp:profile:";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    public async Task<bool> IsStampValidAsync(Guid userId, string stamp, Guid? profileId, string? profileStamp)
    {
        if (string.IsNullOrEmpty(stamp))
        {
            return false;
        }

        var currentUserStamp = await GetUserStampAsync(userId);
        if (currentUserStamp is null || !string.Equals(currentUserStamp, stamp, StringComparison.Ordinal))
        {
            return false;
        }

        if (profileId.HasValue)
        {
            if (string.IsNullOrEmpty(profileStamp))
            {
                return false;
            }

            var currentProfileStamp = await GetProfileStampAsync(profileId.Value);
            if (currentProfileStamp is null || !string.Equals(currentProfileStamp, profileStamp, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string?> GetUserStampAsync(Guid userId)
    {
        var key = UserStampCachePrefix + userId.ToString("N");
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var stamp = await repository.GetUserSecurityStampAsync(userId);
        if (stamp is not null)
        {
            cache.Set(key, stamp, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime });
        }
        return stamp;
    }

    private async Task<string?> GetProfileStampAsync(Guid profileId)
    {
        var key = ProfileStampCachePrefix + profileId.ToString("N");
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var stamp = await repository.GetProfileSecurityStampAsync(profileId);
        if (stamp is not null)
        {
            cache.Set(key, stamp, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime });
        }
        return stamp;
    }
}
