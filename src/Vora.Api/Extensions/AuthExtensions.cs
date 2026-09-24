using System.Security.Claims;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;

namespace Vora.Api.Extensions;

public static class AuthExtensions
{
    public static Guid? GetProfileId(this ClaimsPrincipal user)
    {
        var subClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        var accountIdClaim = user.FindFirst("accountId");

        if (accountIdClaim != null && Guid.TryParse(accountIdClaim.Value, out _))
        {
            if (subClaim != null && Guid.TryParse(subClaim.Value, out var profileId))
            {
                return profileId;
            }
            return null;
        }

        if (subClaim != null && Guid.TryParse(subClaim.Value, out var legacyId))
        {
            return legacyId;
        }

        return null;
    }

    public static Guid? GetAccountId(this ClaimsPrincipal user)
    {
        var accountIdClaim = user.FindFirst("accountId");
        if (accountIdClaim != null && Guid.TryParse(accountIdClaim.Value, out var accountId))
        {
            return accountId;
        }

        var subClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        if (subClaim != null && Guid.TryParse(subClaim.Value, out var legacyId))
        {
            return legacyId;
        }

        return null;
    }

    public static bool HasAllLibraryAccess(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("hasAllLibraryAccess");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }

    public static List<Guid> GetAllowedLibraryIds(this ClaimsPrincipal user)
    {
        return user.FindAll("allowedLibrary")
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    public static List<string> GetAllowedMovieRatings(this ClaimsPrincipal user)
    {
        return user.FindAll("allowedMovieRating").Select(c => c.Value).ToList();
    }

    public static List<string> GetAllowedTvRatings(this ClaimsPrincipal user)
    {
        return user.FindAll("allowedTvRating").Select(c => c.Value).ToList();
    }

    public static List<string> GetAllowedMusicRatings(this ClaimsPrincipal user)
    {
        return user.FindAll("allowedMusicRating").Select(c => c.Value).ToList();
    }

    public static bool BlockUnratedContent(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("blockUnrated");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }

    // The one place a request's music access is decided. The music and smart
    // playlist endpoints each built their own copy of this, and both passed the
    // profile's cross-media hasAllRatings through — so restricting a child's
    // movies silently restricted their music to untagged tracks only. Whether
    // music is restricted now follows from the music allowlist alone, which
    // MusicAccessFilter computes rather than accepting.
    public static MusicAccessFilter GetMusicAccessFilter(this ClaimsPrincipal user) => new()
    {
        HasAllLibraryAccess = user.HasAllLibraryAccess(),
        AllowedLibraryIds = user.GetAllowedLibraryIds(),
        AllowedRatings = user.GetAllowedMusicRatings(),
        BlockUnratedContent = user.BlockUnratedContent()
    };

    public static PlaylistAccessFilter GetPlaylistAccessFilter(this ClaimsPrincipal user) => new()
    {
        HasAllLibraryAccess = user.HasAllLibraryAccess(),
        AllowedLibraryIds = user.GetAllowedLibraryIds(),
        AllowedMovieRatings = user.GetAllowedMovieRatings(),
        AllowedTvRatings = user.GetAllowedTvRatings(),
        AllowedMusicRatings = user.GetAllowedMusicRatings(),
        BlockUnratedContent = user.BlockUnratedContent()
    };

    public static bool CanTimeshiftIptv(this ClaimsPrincipal user)
    {
        if (user.IsAdmin()) return true;
        var claim = user.FindFirst("canTimeshiftIptv");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }

    public static bool CanRecordLiveTv(this ClaimsPrincipal user)
    {
        if (user.IsAdmin()) return true;
        var claim = user.FindFirst("canRecordLiveTv");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }

    public static bool CanAddCustomPodcastFeeds(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("canAddCustomPodcastFeeds");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("isAdmin");
        return claim != null && bool.TryParse(claim.Value, out var value) && value;
    }
}
