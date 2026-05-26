using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Vora.Application.Auth.Dtos;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth;

public enum PasswordResetResult
{
    Success,
    InvalidToken,
    PasswordRejected
}

public interface IAuthManager
{
    Task<(bool IsClaimed, RegistrationMode Mode)> GetSetupStatusAsync();
    Task<AuthResponseDto> ClaimServerAsync(string email, string password, string displayName);
    Task<AuthResponseDto?> LoginAsync(string email, string password);
    Task<string?> GenerateProfileTokenAsync(Guid accountId, Guid profileId);
    Task<AuthResponseDto> RegisterAsync(string email, string password, string displayName, string? secretCode, string? inviteToken = null);
    Task<string> GenerateInviteCodeAsync();
    Task RequestPasswordResetAsync(string email, string requestOriginFallback, CancellationToken cancellationToken = default);
    Task<PasswordResetResult> ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

public class AuthManager(
    IUserRepository repository,
    ISystemSettingsRepository settingsRepo,
    IOptions<JwtOptions> jwtOptions,
    IEmailService emailService,
    IInvitationManager invitationManager,
    IMemoryCache memoryCache,
    ILogger<AuthManager> logger) : IAuthManager
{
    private const int AccountTokenLifetimeHours = 2;
    private const int ProfileTokenLifetimeDays = 7;
    private const int InviteCodeLifetimeMinutes = 30;
    private const int PasswordResetTicketLifetimeMinutes = 60;
    private const int PasswordResetRequestsPerHour = 3;
    private const int MinPasswordLength = 8;
    private const string PasswordResetThrottlePrefix = "pwreset:throttle:";

    private const int InvitePinLength = 4;

    public async Task<(bool IsClaimed, RegistrationMode Mode)> GetSetupStatusAsync()
    {
        var isClaimed = await repository.HasAdminUserAsync();
        var settings = await settingsRepo.GetSettingsAsync();
        return (isClaimed, settings.RegistrationMode);
    }

    public async Task<string> GenerateInviteCodeAsync()
    {
        try
        {
            var code = GeneratePin(InvitePinLength);

            var ticket = new RegistrationTicket
            {
                SecretCode = code,
                ExpiresAt = DateTime.UtcNow.AddMinutes(InviteCodeLifetimeMinutes)
            };

            await repository.CreateRegistrationTicketAsync(ticket);
            return code;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate invite code.");
            throw;
        }
    }

    public async Task<AuthResponseDto> RegisterAsync(string email, string password, string displayName, string? secretCode, string? inviteToken = null)
    {
        EnsurePasswordMeetsPolicy(password);
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(inviteToken))
        {
            var invitation = await invitationManager.ValidateTokenAsync(inviteToken);
            if (invitation is null)
            {
                throw new InvalidOperationException("Invitation is invalid or has expired.");
            }

            if (!string.Equals(invitation.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The email address must match the address the invitation was sent to.");
            }

            var existingUserByInvite = await repository.GetUserWithProfilesByEmailAsync(normalizedEmail);
            if (existingUserByInvite != null)
            {
                throw new InvalidOperationException("Email is already registered.");
            }

            var invitedUser = BuildUser(normalizedEmail, password, displayName, isAdmin: false);
            try
            {
                await repository.AddUserAsync(invitedUser);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register invited user {Email}", normalizedEmail);
                throw;
            }

            await invitationManager.ConsumeAsync(invitation.TokenHash);
            return BuildAuthResponse(invitedUser);
        }

        var status = await GetSetupStatusAsync();

        if (status.Mode == RegistrationMode.Disabled)
        {
            throw new InvalidOperationException("Registration is currently disabled by the server administrator.");
        }

        if (status.Mode == RegistrationMode.Invitation)
        {
            throw new InvalidOperationException("Registration is invitation-only on this server. Ask the administrator to send you an email invitation.");
        }

        if (status.Mode == RegistrationMode.SecretWord)
        {
            if (string.IsNullOrWhiteSpace(secretCode))
            {
                throw new InvalidOperationException("A secret invite code is required to register.");
            }

            var ticket = await repository.GetRegistrationTicketAsync(secretCode);
            if (ticket == null || ticket.ExpiresAt < DateTime.UtcNow)
            {
                throw new InvalidOperationException("Invalid or expired invite code.");
            }

            await repository.DeleteRegistrationTicketAsync(ticket);
        }

        var existingUser = await repository.GetUserWithProfilesByEmailAsync(normalizedEmail);
        if (existingUser != null)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var user = BuildUser(normalizedEmail, password, displayName, isAdmin: false);
        try
        {
            await repository.AddUserAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register user {Email}", normalizedEmail);
            throw;
        }

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto> ClaimServerAsync(string email, string password, string displayName)
    {
        EnsurePasswordMeetsPolicy(password);
        var status = await GetSetupStatusAsync();
        if (status.IsClaimed)
        {
            throw new InvalidOperationException("Server is already claimed.");
        }

        var admin = BuildUser(email, password, displayName, isAdmin: true, canRequestMedia: true, canTimeshiftIptv: true, canAddCustomPodcastFeeds: true, canRecordLiveTv: true);
        try
        {
            await repository.AddUserAsync(admin);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to claim server with admin email {Email}", email);
            throw;
        }

        return BuildAuthResponse(admin);
    }

    public async Task<AuthResponseDto?> LoginAsync(string email, string password)
    {
        var user = await repository.GetUserWithProfilesByEmailAsync(email);
        if (user == null || !VerifyPassword(password, user.PasswordHash))
        {
            return null;
        }

        return BuildAuthResponse(user);
    }

    public async Task<string?> GenerateProfileTokenAsync(Guid accountId, Guid profileId)
    {
        var profile = await repository.GetProfileWithUserAsync(accountId, profileId);
        if (profile == null)
        {
            return null;
        }

        var userAccount = profile.User;
        var effectiveHasAllAccess = userAccount.HasAllLibraryAccess && profile.HasAllLibraryAccess;
        var effectiveAllowedLibs = ResolveEffectiveLibraries(userAccount, profile, effectiveHasAllAccess);
        var hasAllRatings = profile.AllowedMovieRatings.Count == 0
            && profile.AllowedTvRatings.Count == 0
            && profile.AllowedMusicRatings.Count == 0;
        var schedulesJson = JsonSerializer.Serialize(profile.AccessSchedules.Select(s => new
        {
            dayOfWeek = s.DayOfWeek,
            startTime = s.StartTime,
            endTime = s.EndTime
        }));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, profile.Id.ToString()),
            new("accountId", accountId.ToString()),
            new("isAdmin", userAccount.IsAdmin.ToString()),
            new("stamp", userAccount.SecurityStamp),
            new("profileStamp", profile.SecurityStamp),
            new("hasAllLibraryAccess", effectiveHasAllAccess.ToString()),
            new("accessSchedules", schedulesJson),
            new("hasAllRatings", hasAllRatings.ToString()),
            new("blockUnrated", profile.BlockUnratedContent.ToString()),
            new("canTimeshiftIptv", userAccount.CanTimeshiftIptv.ToString()),
            new("canRecordLiveTv", userAccount.CanRecordLiveTv.ToString()),
            new("canAddCustomPodcastFeeds", ((userAccount.IsAdmin || userAccount.CanAddCustomPodcastFeeds) && (profile.IsAdmin || profile.CanAddCustomPodcastFeeds)).ToString())
        };

        if (!effectiveHasAllAccess)
        {
            claims.AddRange(effectiveAllowedLibs.Select(libId => new Claim("allowedLibrary", libId.ToString())));
        }

        if (!hasAllRatings)
        {
            claims.AddRange(profile.AllowedMovieRatings.Select(r => new Claim("allowedMovieRating", r)));
            claims.AddRange(profile.AllowedTvRatings.Select(r => new Claim("allowedTvRating", r)));
            claims.AddRange(profile.AllowedMusicRatings.Select(r => new Claim("allowedMusicRating", r)));
        }

        return CreateToken(claims, TimeSpan.FromDays(ProfileTokenLifetimeDays));
    }

    private string GenerateAccountToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("accountId", user.Id.ToString()),
            new Claim("stamp", user.SecurityStamp),
            new Claim("type", "account_level")
        };
        return CreateToken(claims, TimeSpan.FromHours(AccountTokenLifetimeHours));
    }

    private string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var jwt = jwtOptions.Value;
        var secret = jwt.SecretKey
            ?? throw new InvalidOperationException("JWT Secret Key is missing from configuration.");
        var issuer = jwt.Issuer
            ?? throw new InvalidOperationException("JWT Issuer is missing from configuration.");
        var audience = jwt.Audience
            ?? throw new InvalidOperationException("JWT Audience is missing from configuration.");
        var key = Encoding.UTF8.GetBytes(secret);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private AuthResponseDto BuildAuthResponse(User user) => new()
    {
        AccessToken = GenerateAccountToken(user),
        UserId = user.Id,
        DisplayName = user.DisplayName,
        IsAdmin = user.IsAdmin
    };

    private static List<Guid> ResolveEffectiveLibraries(User user, UserProfile profile, bool hasAllAccess)
    {
        if (hasAllAccess)
        {
            return new List<Guid>();
        }

        if (user.HasAllLibraryAccess)
        {
            return profile.AllowedLibraryIds;
        }

        if (profile.HasAllLibraryAccess)
        {
            return user.AllowedLibraryIds;
        }

        return user.AllowedLibraryIds.Intersect(profile.AllowedLibraryIds).ToList();
    }

    private static User BuildUser(string email, string password, string displayName, bool isAdmin, bool canRequestMedia = false, bool canTimeshiftIptv = false, bool canAddCustomPodcastFeeds = false, bool canRecordLiveTv = false)
    {
        var user = new User
        {
            Email = email.ToLower(),
            DisplayName = displayName,
            PasswordHash = HashPassword(password),
            IsAdmin = isAdmin,
            CanRequestMedia = canRequestMedia,
            CanTimeshiftIptv = canTimeshiftIptv,
            CanAddCustomPodcastFeeds = canAddCustomPodcastFeeds,
            CanRecordLiveTv = canRecordLiveTv,
        };

        user.Profiles.Add(new UserProfile
        {
            UserId = user.Id,
            Name = displayName,
            User = user,
            IsAdmin = true
        });

        return user;
    }

    public async Task RequestPasswordResetAsync(string email, string requestOriginFallback, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (!TryRecordResetThrottle(normalizedEmail))
        {
            logger.LogInformation("Password reset request throttled for {Email}", normalizedEmail);
            return;
        }

        var settings = await settingsRepo.GetSettingsAsync();
        if (!settings.EmailEnabled)
        {
            logger.LogDebug("Password reset requested for {Email} but email is disabled; ignoring", normalizedEmail);
            return;
        }

        var user = await repository.GetUserWithProfilesByEmailAsync(normalizedEmail);
        if (user is null)
        {
            return;
        }

        await repository.InvalidateOutstandingPasswordResetTicketsForUserAsync(user.Id);

        var token = GenerateResetToken();
        var ticket = new PasswordResetTicket
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddMinutes(PasswordResetTicketLifetimeMinutes)
        };

        await repository.CreatePasswordResetTicketAsync(ticket);

        var resetLink = BuildResetLink(settings.EmailPublicBaseUrl, requestOriginFallback, token);

        try
        {
            await emailService.SendAsync(new EmailMessage
            {
                TemplateKey = EmailTemplateKey.PasswordReset,
                ToAddress = user.Email,
                ToDisplayName = user.DisplayName,
                Variables = new Dictionary<string, string>
                {
                    [EmailTemplateVariables.ServerName] = string.IsNullOrWhiteSpace(settings.ServerName) ? "Vora" : settings.ServerName,
                    [EmailTemplateVariables.UserName] = user.DisplayName,
                    [EmailTemplateVariables.ResetLink] = resetLink
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue password reset email for {Email}", normalizedEmail);
        }
    }

    public async Task<PasswordResetResult> ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return PasswordResetResult.InvalidToken;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinPasswordLength)
        {
            return PasswordResetResult.PasswordRejected;
        }

        var hash = HashToken(token);
        var ticket = await repository.GetActivePasswordResetTicketByHashAsync(hash);
        if (ticket is null)
        {
            return PasswordResetResult.InvalidToken;
        }

        var user = await repository.GetUserByIdAsync(ticket.UserId);
        if (user is null)
        {
            await repository.DeletePasswordResetTicketAsync(ticket);
            return PasswordResetResult.InvalidToken;
        }

        user.PasswordHash = HashPassword(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await repository.UpdateUserAsync(user);

        await repository.InvalidateOutstandingPasswordResetTicketsForUserAsync(user.Id);

        return PasswordResetResult.Success;
    }

    private bool TryRecordResetThrottle(string normalizedEmail)
    {
        var key = PasswordResetThrottlePrefix + normalizedEmail;
        if (memoryCache.TryGetValue<ResetThrottleEntry>(key, out var existing) && existing is not null)
        {
            if (existing.Count >= PasswordResetRequestsPerHour) return false;
            existing.Count++;
            return true;
        }

        memoryCache.Set(
            key,
            new ResetThrottleEntry { Count = 1 },
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }
        );
        return true;
    }

    private static string GenerateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildResetLink(string? configuredBaseUrl, string fallbackOrigin, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? fallbackOrigin : configuredBaseUrl;
        baseUrl = baseUrl.TrimEnd('/');
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
    }

    private sealed class ResetThrottleEntry
    {
        public int Count;
    }

    private static string GeneratePin(int length)
    {
        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)('0' + (buffer[i] % 10));
        }
        return new string(chars);
    }
    private static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
    private static bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    private static void EnsurePasswordMeetsPolicy(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            throw new InvalidOperationException($"Password must be at least {MinPasswordLength} characters.");
        }
    }
}
