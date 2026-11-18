using Vora.Domain.Enums;

namespace Vora.Application.Auth.ViewModels;

public class AuthProviderAdminVM
{
    public Guid Id { get; set; }
    public AuthProviderType Type { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public string? Issuer { get; set; }
    public string? ClientId { get; set; }
    public bool HasSecret { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public string EmailClaim { get; set; } = string.Empty;
    public string NameClaim { get; set; } = string.Empty;
    public string GroupsClaim { get; set; } = string.Empty;
    public AuthNewUserPolicy NewUserPolicy { get; set; }
    public AuthLinkByEmailPolicy LinkByEmail { get; set; }
    public List<string> AllowedEmailDomains { get; set; } = new();
    public string? RequiredGroup { get; set; }
    public string? AdminGroup { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveAuthProviderRequest
{
    public AuthProviderType Type { get; set; } = AuthProviderType.Oidc;
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public string? Issuer { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool ClearClientSecret { get; set; }
    public string? Scopes { get; set; }
    public string? EmailClaim { get; set; }
    public string? NameClaim { get; set; }
    public string? GroupsClaim { get; set; }
    public AuthNewUserPolicy NewUserPolicy { get; set; } = AuthNewUserPolicy.FollowRegistrationMode;
    public AuthLinkByEmailPolicy LinkByEmail { get; set; } = AuthLinkByEmailPolicy.ConfirmWithPassword;
    public List<string>? AllowedEmailDomains { get; set; }
    public string? RequiredGroup { get; set; }
    public string? AdminGroup { get; set; }
}

public class AuthProviderPublicVM
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AuthProviderType Type { get; set; }
    public string? IconUrl { get; set; }
}

public class AuthProvidersVM
{
    public List<AuthProviderPublicVM> Providers { get; set; } = new();
    public bool PasswordSignInEnabled { get; set; }
    public RegistrationMode RegistrationMode { get; set; }
}

public class AuthSettingsVM
{
    public bool PasswordSignInEnabled { get; set; }
    public string? PublicBaseUrl { get; set; }
    public string EffectiveBaseUrl { get; set; } = string.Empty;
}

public class UpdateAuthSettingsRequest
{
    public bool PasswordSignInEnabled { get; set; }
    public string? PublicBaseUrl { get; set; }
}

public class LinkedIdentityVM
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
}
