using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class UserRepository(VoraDbContext context) : IUserRepository
{
    public Task<List<UserVM>> GetAllProjectedUsersAsync() =>
        context.Users
            .AsNoTracking()
            .Select(UserVM.Projection)
            .ToListAsync();

    public Task<T?> GetProjectedUserByIdAsync<T>(Guid id, Expression<Func<User, T>> projection) =>
        context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(projection)
            .FirstOrDefaultAsync();

    public Task<T?> GetProjectedUserByEmailAsync<T>(string email, Expression<Func<User, T>> projection)
    {
        var emailLower = email.ToLower();
        return context.Users
            .AsNoTracking()
            .Where(u => u.Email.ToLower() == emailLower)
            .Select(projection)
            .FirstOrDefaultAsync();
    }

    public Task<T?> GetProjectedProfileByIdAsync<T>(Guid profileId, Expression<Func<UserProfile, T>> projection) =>
        context.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(projection)
            .FirstOrDefaultAsync();

    public Task<bool> HasAdminUserAsync() =>
        context.Users.AnyAsync(u => u.IsAdmin);

    public Task<User?> GetUserByIdAsync(Guid id) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<User?> GetUserWithProfilesByEmailAsync(string email)
    {
        var emailLower = email.ToLower();
        return context.Users
            .Include(u => u.Profiles)
                .ThenInclude(p => p.AccessSchedules)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower);
    }

    public Task<User?> GetUserForProfileAsync(Guid profileId) =>
        context.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => p.User)
            .FirstOrDefaultAsync();

    public Task<UserProfile?> GetProfileByIdAsync(Guid id) =>
        context.UserProfiles
            .Include(p => p.AccessSchedules)
            .FirstOrDefaultAsync(p => p.Id == id);

    public Task<UserProfile?> GetProfileWithUserAsync(Guid accountId, Guid profileId) =>
        context.UserProfiles
            .Include(p => p.User)
            .Include(p => p.AccessSchedules)
            .FirstOrDefaultAsync(p => p.Id == profileId && p.UserId == accountId);

    public Task<string?> GetUserSecurityStampAsync(Guid userId) =>
        context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync();

    public Task<string?> GetProfileSecurityStampAsync(Guid profileId) =>
        context.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => p.SecurityStamp)
            .FirstOrDefaultAsync();

    public async Task AddUserAsync(User user)
    {
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
    }

    public async Task UpdateUserAsync(User user)
    {
        context.Users.Update(user);
        await context.SaveChangesAsync();
    }

    public async Task AddProfileAsync(UserProfile profile)
    {
        await context.UserProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
    }

    public async Task UpdateProfileAsync(UserProfile profile)
    {
        context.UserProfiles.Update(profile);
        await context.SaveChangesAsync();
    }

    public async Task DeleteProfileAsync(Guid id)
    {
        var profile = await context.UserProfiles.FindAsync(id);
        if (profile == null)
        {
            return;
        }

        context.UserProfiles.Remove(profile);
        await context.SaveChangesAsync();
    }

    public async Task ReplaceProfileSchedulesAsync(Guid profileId, IEnumerable<ProfileAccessSchedule> schedules)
    {
        var existing = await context.Set<ProfileAccessSchedule>()
            .Where(s => s.UserProfileId == profileId)
            .ToListAsync();

        context.Set<ProfileAccessSchedule>().RemoveRange(existing);
        await context.Set<ProfileAccessSchedule>().AddRangeAsync(schedules);
        await context.SaveChangesAsync();
    }

    public async Task CreateRegistrationTicketAsync(RegistrationTicket ticket)
    {
        await context.RegistrationTickets.AddAsync(ticket);
        await context.SaveChangesAsync();
    }

    public Task<RegistrationTicket?> GetRegistrationTicketAsync(string secretCode)
    {
        var normalized = secretCode.ToLower().Trim();
        return context.RegistrationTickets.FirstOrDefaultAsync(t => t.SecretCode.ToLower() == normalized);
    }

    public async Task DeleteRegistrationTicketAsync(RegistrationTicket ticket)
    {
        context.RegistrationTickets.Remove(ticket);
        await context.SaveChangesAsync();
    }

    public async Task CreatePasswordResetTicketAsync(PasswordResetTicket ticket)
    {
        await context.PasswordResetTickets.AddAsync(ticket);
        await context.SaveChangesAsync();
    }

    public Task<PasswordResetTicket?> GetActivePasswordResetTicketByHashAsync(string tokenHash) =>
        context.PasswordResetTickets
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.ExpiresAt > DateTime.UtcNow);

    public async Task DeletePasswordResetTicketAsync(PasswordResetTicket ticket)
    {
        context.PasswordResetTickets.Remove(ticket);
        await context.SaveChangesAsync();
    }

    public async Task InvalidateOutstandingPasswordResetTicketsForUserAsync(Guid userId)
    {
        var outstanding = await context.PasswordResetTickets
            .Where(t => t.UserId == userId)
            .ToListAsync();
        if (outstanding.Count == 0) return;
        context.PasswordResetTickets.RemoveRange(outstanding);
        await context.SaveChangesAsync();
    }

    public async Task CreateEmailChangeTicketAsync(EmailChangeTicket ticket)
    {
        await context.EmailChangeTickets.AddAsync(ticket);
        await context.SaveChangesAsync();
    }

    public Task<EmailChangeTicket?> GetActiveEmailChangeTicketByHashAsync(string tokenHash) =>
        context.EmailChangeTickets
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.ExpiresAt > DateTime.UtcNow);

    public async Task DeleteEmailChangeTicketAsync(EmailChangeTicket ticket)
    {
        context.EmailChangeTickets.Remove(ticket);
        await context.SaveChangesAsync();
    }

    public async Task InvalidateOutstandingEmailChangeTicketsForUserAsync(Guid userId)
    {
        var outstanding = await context.EmailChangeTickets
            .Where(t => t.UserId == userId)
            .ToListAsync();
        if (outstanding.Count == 0) return;
        context.EmailChangeTickets.RemoveRange(outstanding);
        await context.SaveChangesAsync();
    }

    public async Task ApplyEmailChangeAsync(User user)
    {
        var outstanding = await context.EmailChangeTickets
            .Where(t => t.UserId == user.Id)
            .ToListAsync();
        context.Users.Update(user);
        if (outstanding.Count > 0)
        {
            context.EmailChangeTickets.RemoveRange(outstanding);
        }
        await context.SaveChangesAsync();
    }

    public async Task AddAuthRefreshTokenAsync(AuthRefreshToken token)
    {
        await context.AuthRefreshTokens.AddAsync(token);
        await context.SaveChangesAsync();
    }

    public Task<AuthRefreshToken?> GetAuthRefreshTokenByHashAsync(string tokenHash) =>
        context.AuthRefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task UpdateAuthRefreshTokenAsync(AuthRefreshToken token)
    {
        context.AuthRefreshTokens.Update(token);
        await context.SaveChangesAsync();
    }

    public async Task RevokeAuthRefreshTokenFamilyAsync(Guid familyId)
    {
        await context.AuthRefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
    }

    public async Task RevokeAuthRefreshTokensForDeviceAsync(Guid userId, string deviceId)
    {
        await context.AuthRefreshTokens
            .Where(t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
    }

    public Task<(List<UserProfileHistoryDto> Data, int Total)> GetUserPlayHistoryAsync(
        Guid userId,
        Guid? profileId,
        int page,
        int pageSize,
        string search,
        string typeFilter) =>
        UserPlayHistoryProjection.LoadAsync(context, userId, profileId, page, pageSize, search, typeFilter);

    public Task<string?> GetProfileDeviceNavPrefsAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.NavPrefsJson);

    public Task SaveProfileDeviceNavPrefsAsync(Guid profileId, string deviceId, string navPrefsJson) =>
        UpsertProfileDeviceSettingAsync(profileId, deviceId, s => s.NavPrefsJson = navPrefsJson);

    public Task<string?> GetProfileDevicePlaybackPrefsAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.PlaybackPrefs);

    public Task SaveProfileDeviceSettingsAsync(Guid profileId, string deviceId, string playbackPrefs, string iptvPrefs) =>
        UpsertProfileDeviceSettingAsync(profileId, deviceId, s =>
        {
            s.PlaybackPrefs = playbackPrefs;
            s.IptvPrefsJson = iptvPrefs;
        });

    public Task<string?> GetProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.DiscoveryLayoutJson);

    public Task SaveProfileDeviceDiscoveryLayoutAsync(Guid profileId, string deviceId, string layoutJson) =>
        UpsertProfileDeviceSettingAsync(profileId, deviceId, s => s.DiscoveryLayoutJson = layoutJson);

    public Task<string?> GetProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.HomeLayoutJson);

    public Task SaveProfileDeviceHomeLayoutAsync(Guid profileId, string deviceId, string layoutJson) =>
        UpsertProfileDeviceSettingAsync(profileId, deviceId, s => s.HomeLayoutJson = layoutJson);

    public Task<string?> GetProfileDeviceIptvPrefsAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.IptvPrefsJson);

    public Task<string?> GetProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId) =>
        ReadProfileDeviceFieldAsync(profileId, deviceId, s => s.RadioPrefsJson);

    public Task SaveProfileDeviceRadioPrefsAsync(Guid profileId, string deviceId, string radioPrefsJson) =>
        UpsertProfileDeviceSettingAsync(profileId, deviceId, s => s.RadioPrefsJson = radioPrefsJson);

    public async Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId)
    {
        var rowsAffected = await context.UserMediaStates
            .Where(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHiddenFromContinueWatching, true));

        if (rowsAffected > 0)
        {
            return;
        }

        context.UserMediaStates.Add(new UserMediaState
        {
            ProfileId = profileId,
            MediaItemId = mediaItemId,
            IsHiddenFromContinueWatching = true,
            LastPlayedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private Task<string?> ReadProfileDeviceFieldAsync(Guid profileId, string deviceId, Expression<Func<ProfileDeviceSetting, string?>> fieldSelector) =>
        context.Set<ProfileDeviceSetting>()
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.DeviceId == deviceId)
            .Select(fieldSelector)
            .FirstOrDefaultAsync();

    private async Task UpsertProfileDeviceSettingAsync(Guid profileId, string deviceId, Action<ProfileDeviceSetting> apply)
    {
        var setting = await context.Set<ProfileDeviceSetting>()
            .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.DeviceId == deviceId);

        if (setting == null)
        {
            setting = new ProfileDeviceSetting { ProfileId = profileId, DeviceId = deviceId };
            apply(setting);
            context.Set<ProfileDeviceSetting>().Add(setting);
        }
        else
        {
            apply(setting);
        }

        await context.SaveChangesAsync();
    }
}
