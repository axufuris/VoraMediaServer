using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Vora.Api.Tests.Infra;

internal static class JwtTestHelpers
{
    // Must mirror VoraApiTestFactory's in-memory configuration.
    public const string Secret = "vora-testing-secret-must-be-at-least-32-bytes-long-aaaa";
    public const string Issuer = "VoraMediaServer";
    public const string Audience = "VoraMediaServer";

    // The AdminOnly policy is RequireClaim("isAdmin", "True") — capital T. The boolean
    // ToString() default in .NET matches this exactly.
    public static string IssueAccountToken(Guid accountId, bool isAdmin = false, string accountStamp = "test-account-stamp")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new("accountId", accountId.ToString()),
            new("stamp", accountStamp),
            new("isAdmin", isAdmin.ToString()),
            new("type", "account_level")
        };
        return Build(claims);
    }

    public static string IssueProfileToken(
        Guid accountId,
        Guid profileId,
        bool isAdmin = false,
        bool hasAllLibraryAccess = true,
        bool hasAllRatings = true,
        string accountStamp = "test-account-stamp",
        string profileStamp = "test-profile-stamp")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, profileId.ToString()),
            new("accountId", accountId.ToString()),
            new("stamp", accountStamp),
            new("profileStamp", profileStamp),
            new("isAdmin", isAdmin.ToString()),
            new("hasAllLibraryAccess", hasAllLibraryAccess.ToString()),
            new("hasAllRatings", hasAllRatings.ToString()),
            new("type", "profile_level")
        };
        return Build(claims);
    }

    private static string Build(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}

internal sealed class AlwaysValidStampValidator : Vora.Application.Auth.IJwtSecurityStampValidator
{
    public Task<bool> IsStampValidAsync(Guid userId, string stamp, Guid? profileId, string? profileStamp)
        => Task.FromResult(true);
}
