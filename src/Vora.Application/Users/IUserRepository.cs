using System.Linq.Expressions;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Users;

public interface IUserRepository
{
    Task<List<UserVM>> GetAllProjectedUsersAsync();
    Task<T?> GetProjectedUserByIdAsync<T>(Guid id, Expression<Func<User, T>> projection);
    Task<T?> GetProjectedUserByEmailAsync<T>(string email, Expression<Func<User, T>> projection);
    Task<T?> GetProjectedProfileByIdAsync<T>(Guid profileId, Expression<Func<UserProfile, T>> projection);
    Task<bool> HasAdminUserAsync();

    Task<User?> GetUserByIdAsync(Guid id);
    Task<User?> GetUserWithProfilesByEmailAsync(string email);
    Task<User?> GetUserForProfileAsync(Guid profileId);
    Task<UserProfile?> GetProfileByIdAsync(Guid id);
    Task<UserProfile?> GetProfileWithUserAsync(Guid accountId, Guid profileId);
    Task<string?> GetUserSecurityStampAsync(Guid userId);
    Task<string?> GetProfileSecurityStampAsync(Guid profileId);

    Task AddUserAsync(User user);
    Task UpdateUserAsync(User user);
    Task AddProfileAsync(UserProfile profile);
    Task UpdateProfileAsync(UserProfile profile);
    Task DeleteProfileAsync(Guid id);
    Task ReplaceProfileSchedulesAsync(Guid profileId, IEnumerable<ProfileAccessSchedule> schedules);

    Task CreateRegistrationTicketAsync(RegistrationTicket ticket);
    Task<RegistrationTicket?> GetRegistrationTicketAsync(string secretCode);
    Task DeleteRegistrationTicketAsync(RegistrationTicket ticket);

    Task CreatePasswordResetTicketAsync(PasswordResetTicket ticket);
    Task<PasswordResetTicket?> GetActivePasswordResetTicketByHashAsync(string tokenHash);
    Task DeletePasswordResetTicketAsync(PasswordResetTicket ticket);
    Task InvalidateOutstandingPasswordResetTicketsForUserAsync(Guid userId);
    Task CreateEmailChangeTicketAsync(EmailChangeTicket ticket);
    Task<EmailChangeTicket?> GetActiveEmailChangeTicketByHashAsync(string tokenHash);
    Task DeleteEmailChangeTicketAsync(EmailChangeTicket ticket);
    Task InvalidateOutstandingEmailChangeTicketsForUserAsync(Guid userId);
    Task ApplyEmailChangeAsync(User user);

    Task AddAuthRefreshTokenAsync(AuthRefreshToken token);
    Task<AuthRefreshToken?> GetAuthRefreshTokenByHashAsync(string tokenHash);
    Task UpdateAuthRefreshTokenAsync(AuthRefreshToken token);
    Task RevokeAuthRefreshTokenFamilyAsync(Guid familyId);
    Task RevokeAuthRefreshTokensForDeviceAsync(Guid userId, string deviceId);

    Task<(List<UserProfileHistoryDto> Data, int Total)> GetUserPlayHistoryAsync(Guid userId, Guid? profileId, int page, int pageSize, string search, string typeFilter);

    Task<string?> GetProfileDeviceNavPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceNavPrefsAsync(Guid profileId, string deviceId, string navPrefsJson);
    Task<string?> GetProfileDevicePlaybackPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceSettingsAsync(Guid profileId, string deviceId, string playbackPrefs, string iptvPrefs);
    Task<string?> GetProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId, string layoutJson);
    Task<string?> GetProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId, string layoutJson);
    Task<string?> GetProfileDeviceIptvPrefsAsync(Guid profileId, string deviceId);
    Task<string?> GetProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId);
    Task SaveProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId, string radioPrefsJson);

    Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId);
}
