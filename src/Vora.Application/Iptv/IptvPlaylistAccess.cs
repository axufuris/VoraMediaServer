using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Application.Iptv;

public static class IptvPlaylistAccess
{
    public static bool Allows(UserVM? user, UserProfileVM? profile, Guid playlistId)
    {
        if (user is null) return false;

        var accountAllows = user.IsAdmin || user.HasAllIptvAccess || user.AllowedIptvPlaylistIds.Contains(playlistId);
        var profileAllows = profile is null || profile.IsAdmin || profile.HasAllIptvAccess || profile.AllowedIptvPlaylistIds.Contains(playlistId);
        return accountAllows && profileAllows;
    }

    public static async Task<bool> AllowsAsync(IUserManager userManager, Guid accountId, Guid? profileId, Guid playlistId)
    {
        var user = await userManager.GetUserAccountAsync(accountId);
        if (user is null) return false;

        if (profileId is not { } id || id == accountId)
        {
            return Allows(user, null, playlistId);
        }

        var profile = user.Profiles.FirstOrDefault(p => p.Id == id);
        return profile is not null && Allows(user, profile, playlistId);
    }
}
