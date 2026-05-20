using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Vora.Application.Auth.Dtos;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth;

public interface IAuthManager
{
    Task<(bool IsClaimed, RegistrationMode Mode)> GetSetupStatusAsync();
    Task<AuthResponseDto> ClaimServerAsync(string email, string password, string displayName);
    Task<AuthResponseDto?> LoginAsync(string email, string password);
    Task<string?> GenerateProfileTokenAsync(Guid accountId, Guid profileId);
    Task<AuthResponseDto> RegisterAsync(string email, string password, string displayName, string? secretCode);
    Task<string> GenerateInviteCodeAsync();
}

public class AuthManager(
    IUserRepository repository,
    ISystemSettingsRepository settingsRepo,
    IConfiguration configuration,
    ILogger<AuthManager> logger) : IAuthManager
{
    private const int AccountTokenLifetimeHours = 2;
    private const int ProfileTokenLifetimeDays = 7;
    private const int InviteCodeLifetimeMinutes = 30;

    private static readonly string[] WordDictionary =
    {
        "apple", "brave", "crane", "dance", "eagle", "flame", "grape", "heart", "image", "juice",
        "kite", "lemon", "magic", "noble", "ocean", "peach", "quill", "river", "stone", "train",
        "uncle", "vivid", "water", "xenon", "yacht", "zebra", "acorn", "brick", "cloud", "dream",
        "earth", "frost", "ghost", "house", "iron", "jelly", "knife", "light", "moon", "night",
        "onion", "plant", "queen", "robin", "snake", "tiger", "unity", "voice", "whale", "yard",
        "amber", "bread", "chair", "desk", "engine", "fruit", "glass", "honey", "island", "jewel"
    };

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
            var code = $"{RandomWord()}-{RandomWord()}-{RandomWord()}";

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

    public async Task<AuthResponseDto> RegisterAsync(string email, string password, string displayName, string? secretCode)
    {
        var status = await GetSetupStatusAsync();

        if (status.Mode == RegistrationMode.Disabled)
        {
            throw new InvalidOperationException("Registration is currently disabled by the server administrator.");
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

        var existingUser = await repository.GetUserWithProfilesByEmailAsync(email);
        if (existingUser != null)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var user = BuildUser(email, password, displayName, isAdmin: false);
        try
        {
            await repository.AddUserAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register user {Email}", email);
            throw;
        }

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponseDto> ClaimServerAsync(string email, string password, string displayName)
    {
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
            new Claim("type", "account_level")
        };
        return CreateToken(claims, TimeSpan.FromHours(AccountTokenLifetimeHours));
    }

    private string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var secret = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT Secret Key is missing from configuration.");
        var key = Encoding.ASCII.GetBytes(secret);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime),
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

    private static string RandomWord() => WordDictionary[Random.Shared.Next(WordDictionary.Length)];
    private static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
    private static bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
