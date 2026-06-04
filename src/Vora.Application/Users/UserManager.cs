using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Users;

public interface IUserManager
{
    Task<List<UserVM>> GetAllUsersAsync();
    Task<UserVM?> GetUserAccountAsync(Guid userId);
    Task<bool> ValidateProfilePinAsync(Guid profileId, string pin);
    Task<Guid> CreateManagedProfileAsync(Guid primaryUserId, string name, string? imageUrl, string? pin, List<string> allowedMovieRatings, List<string> allowedTvRatings, List<string> allowedMusicRatings, bool hasAllLibraryAccess, bool blockUnrated, List<Guid> allowedLibraries, bool hasAllIptvAccess, List<Guid> allowedIptvPlaylists, List<ProfileScheduleVM> schedules, bool canAddCustomPodcastFeeds, string? showtimesLocation);
    Task UpdateUserAccountAsync(Guid userId, Guid callingAccountId, bool callerIsAdmin, string email, string displayName, string? newPassword, bool? emailNotifyOnRequestAvailable = null);
    Task UpdateManagedProfileAsync(Guid profileId, string name, string? imageUrl, string? pin, List<string> allowedMovieRatings, List<string> allowedTvRatings, List<string> allowedMusicRatings, bool hasAllLibraryAccess, bool blockUnrated, List<Guid> allowedLibraries, bool hasAllIptvAccess, List<Guid> allowedIptvPlaylists, List<ProfileScheduleVM> schedules, bool canAddCustomPodcastFeeds, string? showtimesLocation);
    Task DeleteManagedProfileAsync(Guid profileId);
    Task UpdateUserAccessAsync(Guid userId, bool hasAllLibraryAccess, List<Guid> allowedLibraries, bool canRequest, bool autoApprove, bool enableAiRecommendations, bool hasAllIptvAccess, List<Guid> allowedIptvPlaylists, bool canRecordLiveTv, long dvrStorageQuotaBytes, bool canTimeshiftIptv, bool canAddCustomPodcastFeeds);
    Task<(List<UserProfileHistoryDto> Data, int Total)> GetUserPlayHistoryAsync(Guid userId, Guid? profileId, int page, int pageSize, string search, string typeFilter);

    Task<string?> GetProfileDeviceNavPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceNavPrefsAsync(Guid profileId, string deviceId, string navPrefsJson);
    Task<string?> GetProfileDevicePlaybackPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceSettingsAsync(Guid profileId, string deviceId, string playbackPrefs, string iptvPrefsJson);
    Task<string?> GetProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId, string layoutJson);
    Task<string?> GetProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId, string layoutJson);
    Task<string?> GetProfileDeviceIptvPrefsAsync(Guid profileId, string deviceId);
    Task<string?> GetProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId, string radioPrefsJson);

    Task<string?> GetProfileRadioPrefsAsync(Guid profileId);
    Task SaveProfileRadioPrefsAsync(Guid profileId, string radioPrefsJson);

    Task<string?> GetShowtimesLocationAsync(Guid profileId);
    Task SaveShowtimesLocationAsync(Guid profileId, string? location);

    Task<PlaybackPreferencesVM> GetPlaybackPreferencesAsync(Guid profileId);
    Task<PlaybackPreferencesVM> SavePlaybackPreferencesAsync(Guid profileId, PlaybackPreferencesVM prefs);
}

public class UserManager(
    IUserRepository repository,
    IUserProfileImageService imageService,
    IClientNotifier notifier,
    ILogger<UserManager> logger) : IUserManager
{
    public Task<List<UserVM>> GetAllUsersAsync() =>
        repository.GetAllProjectedUsersAsync();

    public Task<UserVM?> GetUserAccountAsync(Guid userId) =>
        repository.GetProjectedUserByIdAsync(userId, UserVM.Projection);

    public async Task<bool> ValidateProfilePinAsync(Guid profileId, string pin)
    {
        var dbPinHash = await repository.GetProjectedProfileByIdAsync(profileId, p => p.PinHash);
        if (string.IsNullOrEmpty(dbPinHash))
        {
            return true;
        }
        return dbPinHash == HashPin(pin);
    }

    public async Task<Guid> CreateManagedProfileAsync(
        Guid primaryUserId,
        string name,
        string? imageUrl,
        string? pin,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        List<string> allowedMusicRatings,
        bool hasAllLibraryAccess,
        bool blockUnrated,
        List<Guid> allowedLibraries,
        bool hasAllIptvAccess,
        List<Guid> allowedIptvPlaylists,
        List<ProfileScheduleVM> schedules,
        bool canAddCustomPodcastFeeds,
        string? showtimesLocation)
    {
        var profile = new UserProfile
        {
            UserId = primaryUserId,
            Name = name,
            ProfileImageUrl = imageUrl,
            AllowedMovieRatings = allowedMovieRatings ?? new List<string>(),
            AllowedTvRatings = allowedTvRatings ?? new List<string>(),
            AllowedMusicRatings = allowedMusicRatings ?? new List<string>(),
            HasAllLibraryAccess = hasAllLibraryAccess,
            BlockUnratedContent = blockUnrated,
            AllowedLibraryIds = allowedLibraries ?? new List<Guid>(),
            HasAllIptvAccess = hasAllIptvAccess,
            AllowedIptvPlaylistIds = allowedIptvPlaylists ?? new List<Guid>(),
            CanAddCustomPodcastFeeds = canAddCustomPodcastFeeds,
            ShowtimesLocation = string.IsNullOrWhiteSpace(showtimesLocation) ? null : showtimesLocation.Trim()
        };

        if (!string.IsNullOrWhiteSpace(pin))
        {
            profile.PinHash = HashPin(pin);
        }

        foreach (var schedule in BuildScheduleEntities(profile.Id, schedules))
        {
            profile.AccessSchedules.Add(schedule);
        }

        try
        {
            await repository.AddProfileAsync(profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create managed profile for user {UserId}.", primaryUserId);
            throw;
        }

        return profile.Id;
    }

    public async Task UpdateUserAccountAsync(Guid userId, Guid callingAccountId, bool callerIsAdmin, string email, string displayName, string? newPassword, bool? emailNotifyOnRequestAvailable = null)
    {
        if (!callerIsAdmin && callingAccountId != userId)
        {
            throw new UnauthorizedAccessException("You may only update your own account.");
        }

        var user = await repository.GetUserByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        user.Email = email.ToLower();
        user.DisplayName = displayName;
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
        }
        if (emailNotifyOnRequestAvailable.HasValue)
        {
            user.EmailNotifyOnRequestAvailable = emailNotifyOnRequestAvailable.Value;
        }

        try
        {
            await repository.UpdateUserAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update user account {UserId}.", userId);
            throw;
        }
    }

    public async Task UpdateManagedProfileAsync(
        Guid profileId,
        string name,
        string? imageUrl,
        string? pin,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        List<string> allowedMusicRatings,
        bool hasAllLibraryAccess,
        bool blockUnrated,
        List<Guid> allowedLibraries,
        bool hasAllIptvAccess,
        List<Guid> allowedIptvPlaylists,
        List<ProfileScheduleVM> schedules,
        bool canAddCustomPodcastFeeds,
        string? showtimesLocation)
    {
        var profile = await repository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");

        if (profile.ProfileImageUrl != imageUrl)
        {
            imageService.DeleteImage(profile.ProfileImageUrl);
        }

        ApplyProfileUpdates(profile, name, imageUrl, pin, allowedMovieRatings, allowedTvRatings, allowedMusicRatings, hasAllLibraryAccess, blockUnrated, allowedLibraries, hasAllIptvAccess, allowedIptvPlaylists, canAddCustomPodcastFeeds, showtimesLocation);
        profile.SecurityStamp = Guid.NewGuid().ToString("N");

        try
        {
            await repository.UpdateProfileAsync(profile);
            await repository.ReplaceProfileSchedulesAsync(profileId, BuildScheduleEntities(profileId, schedules));
            await notifier.NotifyProfileAccessUpdatedAsync(profileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update managed profile {ProfileId}.", profileId);
            throw;
        }
    }

    public async Task DeleteManagedProfileAsync(Guid profileId)
    {
        var profile = await repository.GetProfileByIdAsync(profileId);
        if (profile != null)
        {
            imageService.DeleteImage(profile.ProfileImageUrl);
            try
            {
                await repository.DeleteProfileAsync(profileId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete profile {ProfileId}.", profileId);
                throw;
            }
        }

        await notifier.NotifyProfileAccessUpdatedAsync(profileId);
    }

    public async Task UpdateUserAccessAsync(
        Guid userId,
        bool hasAllLibraryAccess,
        List<Guid> allowedLibraries,
        bool canRequest,
        bool autoApprove,
        bool enableAiRecommendations,
        bool hasAllIptvAccess,
        List<Guid> allowedIptvPlaylists,
        bool canRecordLiveTv,
        long dvrStorageQuotaBytes,
        bool canTimeshiftIptv,
        bool canAddCustomPodcastFeeds)
    {
        var user = await repository.GetUserByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsAdmin)
        {
            user.HasAllLibraryAccess = true;
            user.AllowedLibraryIds = new List<Guid>();
            user.HasAllIptvAccess = true;
            user.AllowedIptvPlaylistIds = new List<Guid>();
        }
        else
        {
            user.HasAllLibraryAccess = hasAllLibraryAccess;
            user.AllowedLibraryIds = allowedLibraries ?? new List<Guid>();
            user.HasAllIptvAccess = hasAllIptvAccess;
            user.AllowedIptvPlaylistIds = allowedIptvPlaylists ?? new List<Guid>();
        }

        user.CanRequestMedia = canRequest;
        user.AutoApproveRequests = autoApprove;
        user.EnableAiRecommendations = enableAiRecommendations;
        user.CanRecordLiveTv = canRecordLiveTv;
        user.DvrStorageQuotaBytes = dvrStorageQuotaBytes;
        user.CanTimeshiftIptv = user.IsAdmin || canTimeshiftIptv;
        user.CanAddCustomPodcastFeeds = user.IsAdmin || canAddCustomPodcastFeeds;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        try
        {
            await repository.UpdateUserAsync(user);
            await notifier.NotifyUserAccessUpdatedAsync(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update access for user {UserId}.", userId);
            throw;
        }
    }

    public Task<(List<UserProfileHistoryDto> Data, int Total)> GetUserPlayHistoryAsync(Guid userId, Guid? profileId, int page, int pageSize, string search, string typeFilter) =>
        repository.GetUserPlayHistoryAsync(userId, profileId, page, pageSize, search, typeFilter);

    public Task<string?> GetProfileDeviceNavPrefsAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDeviceNavPrefsAsync(profileId, deviceId);

    public Task SaveProfileDeviceNavPrefsAsync(Guid profileId, string deviceId, string navPrefsJson) =>
        repository.SaveProfileDeviceNavPrefsAsync(profileId, deviceId, navPrefsJson);

    public Task<string?> GetProfileDevicePlaybackPrefsAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDevicePlaybackPrefsAsync(profileId, deviceId);

    public Task SaveProfileDeviceSettingsAsync(Guid profileId, string deviceId, string playbackPrefs, string iptvPrefsJson) =>
        repository.SaveProfileDeviceSettingsAsync(profileId, deviceId, playbackPrefs, iptvPrefsJson);

    public Task<string?> GetProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDeviceDiscoveryLayoutAsync(profileId, deviceId);

    public Task SaveProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId, string layoutJson) =>
        repository.SaveProfileDeviceDiscoveryLayoutAsync(profileId, deviceId, layoutJson);

    public Task<string?> GetProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDeviceHomeLayoutAsync(profileId, deviceId);

    public Task SaveProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId, string layoutJson) =>
        repository.SaveProfileDeviceHomeLayoutAsync(profileId, deviceId, layoutJson);

    public Task<string?> GetProfileDeviceIptvPrefsAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDeviceIptvPrefsAsync(profileId, deviceId);

    public Task<string?> GetProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId) =>
        repository.GetProfileDeviceRadioPrefsAsync(profileId, deviceId);

    public Task SaveProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId, string radioPrefsJson) =>
        repository.SaveProfileDeviceRadioPrefsAsync(profileId, deviceId, radioPrefsJson);

    public async Task<string?> GetProfileRadioPrefsAsync(Guid profileId)
    {
        var profile = await repository.GetProfileByIdAsync(profileId);
        return profile?.RadioPrefsJson;
    }

    public async Task SaveProfileRadioPrefsAsync(Guid profileId, string radioPrefsJson)
    {
        var profile = await repository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");
        profile.RadioPrefsJson = radioPrefsJson;
        await repository.UpdateProfileAsync(profile);
        await notifier.NotifyRadioPrefsUpdatedAsync(profileId);
    }

    private static void ApplyProfileUpdates(
        UserProfile profile,
        string name,
        string? imageUrl,
        string? pin,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        List<string> allowedMusicRatings,
        bool hasAllLibraryAccess,
        bool blockUnrated,
        List<Guid> allowedLibraries,
        bool hasAllIptvAccess,
        List<Guid> allowedIptvPlaylists,
        bool canAddCustomPodcastFeeds,
        string? showtimesLocation)
    {
        profile.Name = name;
        profile.ProfileImageUrl = imageUrl;
        profile.AllowedMovieRatings = allowedMovieRatings ?? new List<string>();
        profile.AllowedTvRatings = allowedTvRatings ?? new List<string>();
        profile.AllowedMusicRatings = allowedMusicRatings ?? new List<string>();
        profile.HasAllLibraryAccess = hasAllLibraryAccess;
        profile.BlockUnratedContent = blockUnrated;
        profile.AllowedLibraryIds = allowedLibraries ?? new List<Guid>();
        profile.HasAllIptvAccess = hasAllIptvAccess;
        profile.AllowedIptvPlaylistIds = allowedIptvPlaylists ?? new List<Guid>();
        profile.CanAddCustomPodcastFeeds = canAddCustomPodcastFeeds;
        profile.ShowtimesLocation = string.IsNullOrWhiteSpace(showtimesLocation) ? null : showtimesLocation.Trim();

        if (pin != null)
        {
            profile.PinHash = string.IsNullOrWhiteSpace(pin) ? null : HashPin(pin);
        }
    }

    public async Task<string?> GetShowtimesLocationAsync(Guid profileId)
    {
        var profile = await repository.GetProfileByIdAsync(profileId);
        return profile?.ShowtimesLocation;
    }

    public async Task SaveShowtimesLocationAsync(Guid profileId, string? location)
    {
        var profile = await repository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");
        profile.ShowtimesLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        await repository.UpdateProfileAsync(profile);
    }

    public async Task<PlaybackPreferencesVM> GetPlaybackPreferencesAsync(Guid profileId)
    {
        var profile = await repository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");
        return new PlaybackPreferencesVM
        {
            AutoSkipIntro = profile.AutoSkipIntro,
            AutoSkipCredits = profile.AutoSkipCredits,
            MinimumCreditsSceneSeconds = profile.MinimumCreditsSceneSeconds
        };
    }

    public async Task<PlaybackPreferencesVM> SavePlaybackPreferencesAsync(Guid profileId, PlaybackPreferencesVM prefs)
    {
        var profile = await repository.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException("Profile not found.");
        profile.AutoSkipIntro = prefs.AutoSkipIntro;
        profile.AutoSkipCredits = prefs.AutoSkipCredits;
        profile.MinimumCreditsSceneSeconds = Math.Clamp(prefs.MinimumCreditsSceneSeconds, 0, 600);
        await repository.UpdateProfileAsync(profile);
        return new PlaybackPreferencesVM
        {
            AutoSkipIntro = profile.AutoSkipIntro,
            AutoSkipCredits = profile.AutoSkipCredits,
            MinimumCreditsSceneSeconds = profile.MinimumCreditsSceneSeconds
        };
    }

    private static IEnumerable<ProfileAccessSchedule> BuildScheduleEntities(Guid profileId, IEnumerable<ProfileScheduleVM> schedules)
    {
        foreach (var schedule in schedules)
        {
            if (TimeSpan.TryParse(schedule.StartTime, out var start) && TimeSpan.TryParse(schedule.EndTime, out var end))
            {
                yield return new ProfileAccessSchedule
                {
                    UserProfileId = profileId,
                    DayOfWeek = (DayOfWeek)schedule.DayOfWeek,
                    StartTime = start,
                    EndTime = end
                };
            }
        }
    }

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToBase64String(bytes);
    }
}
