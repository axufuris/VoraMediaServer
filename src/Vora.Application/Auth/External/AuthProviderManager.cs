using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth.External;

public enum AuthProviderSaveOutcome
{
    Saved,
    Invalid,
    SlugInUse,
    NotFound
}

public sealed record AuthProviderSaveResult(AuthProviderSaveOutcome Outcome, AuthProviderAdminVM? Provider, string? Error)
{
    public static AuthProviderSaveResult Saved(AuthProviderAdminVM provider) => new(AuthProviderSaveOutcome.Saved, provider, null);
    public static AuthProviderSaveResult Invalid(string error) => new(AuthProviderSaveOutcome.Invalid, null, error);
    public static AuthProviderSaveResult NotFound { get; } = new(AuthProviderSaveOutcome.NotFound, null, "That sign-in provider does not exist.");
    public static AuthProviderSaveResult SlugInUse { get; } = new(AuthProviderSaveOutcome.SlugInUse, null, "Another sign-in provider already uses that address name.");
}

public enum AuthProviderDeleteOutcome
{
    Deleted,
    NotFound,
    WouldStrandUsers
}

public sealed record AuthProviderDeleteResult(AuthProviderDeleteOutcome Outcome, int StrandedUsers);

public sealed record AuthSettingsSaveResult(bool Saved, string? Error, AuthSettingsVM? Settings);

public interface IAuthProviderManager
{
    Task<IReadOnlyList<AuthProviderAdminVM>> ListAsync(string requestOrigin);
    Task<AuthProviderAdminVM?> GetAsync(Guid id, string requestOrigin);
    Task<AuthProviderSaveResult> CreateAsync(SaveAuthProviderRequest request, string requestOrigin);
    Task<AuthProviderSaveResult> UpdateAsync(Guid id, SaveAuthProviderRequest request, string requestOrigin);
    Task<AuthProviderDeleteResult> DeleteAsync(Guid id, bool force);
    Task<AuthProvidersVM> GetPublicAsync();
    Task<AuthSettingsVM> GetSettingsAsync(string requestOrigin);
    Task<AuthSettingsSaveResult> UpdateSettingsAsync(UpdateAuthSettingsRequest request, string requestOrigin);
}

public sealed partial class AuthProviderManager(
    IAuthProviderRepository providers,
    ISystemSettingsRepository settingsRepo,
    IAuthSecretProtector secretProtector,
    ILogger<AuthProviderManager> logger) : IAuthProviderManager
{
    private const int MaxTextLength = 256;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public async Task<IReadOnlyList<AuthProviderAdminVM>> ListAsync(string requestOrigin)
    {
        var baseUrl = await BaseUrlAsync(requestOrigin);
        return (await providers.GetAllAsync()).Select(p => ToAdminVM(p, baseUrl)).ToList();
    }

    public async Task<AuthProviderAdminVM?> GetAsync(Guid id, string requestOrigin)
    {
        var provider = await providers.GetByIdAsync(id);
        return provider is null ? null : ToAdminVM(provider, await BaseUrlAsync(requestOrigin));
    }

    public async Task<AuthProviderSaveResult> CreateAsync(SaveAuthProviderRequest request, string requestOrigin)
    {
        var provider = new AuthProvider { Slug = string.Empty, DisplayName = string.Empty };
        var error = Apply(provider, request);
        if (error is not null)
        {
            return AuthProviderSaveResult.Invalid(error);
        }

        if (await providers.GetBySlugAsync(provider.Slug) is not null)
        {
            return AuthProviderSaveResult.SlugInUse;
        }

        await providers.AddAsync(provider);
        logger.LogInformation("Added sign-in provider {Provider}", provider.Slug);
        return AuthProviderSaveResult.Saved(ToAdminVM(provider, await BaseUrlAsync(requestOrigin)));
    }

    public async Task<AuthProviderSaveResult> UpdateAsync(Guid id, SaveAuthProviderRequest request, string requestOrigin)
    {
        var provider = await providers.GetByIdAsync(id);
        if (provider is null)
        {
            return AuthProviderSaveResult.NotFound;
        }

        var error = Apply(provider, request);
        if (error is not null)
        {
            return AuthProviderSaveResult.Invalid(error);
        }

        var sameSlug = await providers.GetBySlugAsync(provider.Slug);
        if (sameSlug is not null && sameSlug.Id != provider.Id)
        {
            return AuthProviderSaveResult.SlugInUse;
        }

        provider.UpdatedAt = DateTime.UtcNow;
        await providers.UpdateAsync(provider);
        logger.LogInformation("Updated sign-in provider {Provider}", provider.Slug);
        return AuthProviderSaveResult.Saved(ToAdminVM(provider, await BaseUrlAsync(requestOrigin)));
    }

    public async Task<AuthProviderDeleteResult> DeleteAsync(Guid id, bool force)
    {
        var provider = await providers.GetByIdAsync(id);
        if (provider is null)
        {
            return new AuthProviderDeleteResult(AuthProviderDeleteOutcome.NotFound, 0);
        }

        var stranded = await providers.CountUsersWithoutOtherWayInAsync(id);
        if (stranded > 0 && !force)
        {
            return new AuthProviderDeleteResult(AuthProviderDeleteOutcome.WouldStrandUsers, stranded);
        }

        await providers.DeleteAsync(id);
        logger.LogInformation("Removed sign-in provider {Provider}", provider.Slug);
        return new AuthProviderDeleteResult(AuthProviderDeleteOutcome.Deleted, stranded);
    }

    public async Task<AuthProvidersVM> GetPublicAsync()
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var enabled = await providers.GetEnabledAsync();
        return new AuthProvidersVM
        {
            PasswordSignInEnabled = settings.PasswordSignInEnabled,
            RegistrationMode = settings.RegistrationMode,
            Providers = enabled
                .Where(p => p.Type == AuthProviderType.Oidc)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.DisplayName)
                .Select(p => new AuthProviderPublicVM { Id = p.Id, Slug = p.Slug, DisplayName = p.DisplayName, Type = p.Type, IconUrl = p.IconUrl })
                .ToList()
        };
    }

    public async Task<AuthSettingsVM> GetSettingsAsync(string requestOrigin)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        return ToSettingsVM(settings, requestOrigin);
    }

    public async Task<AuthSettingsSaveResult> UpdateSettingsAsync(UpdateAuthSettingsRequest request, string requestOrigin)
    {
        var publicBaseUrl = string.IsNullOrWhiteSpace(request.PublicBaseUrl) ? null : request.PublicBaseUrl.Trim().TrimEnd('/');
        if (publicBaseUrl is not null && (publicBaseUrl.Length > MaxTextLength || !IsHttpUrl(publicBaseUrl) || publicBaseUrl.Contains('?') || publicBaseUrl.Contains('#')))
        {
            return new AuthSettingsSaveResult(false, "The public address must be a web address such as https://vora.example.com, without a query or fragment.", null);
        }

        if (!request.PasswordSignInEnabled && (await providers.GetEnabledAsync()).All(p => p.Type != AuthProviderType.Oidc))
        {
            return new AuthSettingsSaveResult(false, "Turn on a sign-in provider before turning off password sign-in, or nobody but admins could sign in.", null);
        }

        var settings = await settingsRepo.GetSettingsForUpdateAsync();
        settings.PasswordSignInEnabled = request.PasswordSignInEnabled;
        settings.PublicBaseUrl = publicBaseUrl;
        await settingsRepo.SaveChangesAsync();

        logger.LogInformation("Password sign-in is now {State}", settings.PasswordSignInEnabled ? "on" : "off");
        return new AuthSettingsSaveResult(true, null, ToSettingsVM(settings, requestOrigin));
    }

    private async Task<string> BaseUrlAsync(string requestOrigin)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        return AuthRedirectUris.BaseUrl(settings.PublicBaseUrl, settings.EmailPublicBaseUrl, requestOrigin);
    }

    private static AuthSettingsVM ToSettingsVM(Domain.Entities.Settings.ServerSetting settings, string requestOrigin) => new()
    {
        PasswordSignInEnabled = settings.PasswordSignInEnabled,
        PublicBaseUrl = settings.PublicBaseUrl,
        EffectiveBaseUrl = AuthRedirectUris.BaseUrl(settings.PublicBaseUrl, settings.EmailPublicBaseUrl, requestOrigin)
    };

    private static AuthProviderAdminVM ToAdminVM(AuthProvider provider, string baseUrl) => new()
    {
        Id = provider.Id,
        Type = provider.Type,
        Slug = provider.Slug,
        DisplayName = provider.DisplayName,
        Enabled = provider.Enabled,
        IconUrl = provider.IconUrl,
        SortOrder = provider.SortOrder,
        Issuer = provider.Issuer,
        ClientId = provider.ClientId,
        HasSecret = !string.IsNullOrEmpty(provider.ClientSecretCiphertext),
        Scopes = provider.Scopes,
        EmailClaim = provider.EmailClaim,
        NameClaim = provider.NameClaim,
        GroupsClaim = provider.GroupsClaim,
        NewUserPolicy = provider.NewUserPolicy,
        LinkByEmail = provider.LinkByEmail,
        AllowedEmailDomains = provider.AllowedEmailDomains.ToList(),
        RequiredGroup = provider.RequiredGroup,
        AdminGroup = provider.AdminGroup,
        RedirectUri = AuthRedirectUris.Callback(baseUrl, provider.Slug),
        CreatedAt = provider.CreatedAt,
        UpdatedAt = provider.UpdatedAt
    };

    private string? Apply(AuthProvider provider, SaveAuthProviderRequest request)
    {
        if (request.Type != AuthProviderType.Oidc)
        {
            return "Only OpenID Connect providers can be added for now.";
        }

        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(slug))
        {
            return "The address name may use lower-case letters, digits and hyphens, and must start and end with a letter or digit.";
        }

        var displayName = (request.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0 || displayName.Length > 64)
        {
            return "Enter a name of up to 64 characters for the sign-in button.";
        }

        var issuer = string.IsNullOrWhiteSpace(request.Issuer) ? null : request.Issuer.Trim();
        if (issuer is not null && (issuer.Length > MaxTextLength || !IsHttpUrl(issuer) || issuer.Contains('?') || issuer.Contains('#')))
        {
            return "The issuer must be a web address such as https://auth.example.com/application/o/vora/.";
        }

        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId.Trim();
        if (clientId is not null && clientId.Length > MaxTextLength)
        {
            return "The client id is too long.";
        }

        if (request.Enabled && (issuer is null || clientId is null))
        {
            return "A provider needs its issuer and client id before it can be turned on.";
        }

        var scopes = string.Join(' ', (request.Scopes ?? "openid email profile").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal));
        if (!scopes.Split(' ').Contains("openid", StringComparer.Ordinal))
        {
            return "The scopes must include openid.";
        }

        if (!string.IsNullOrWhiteSpace(request.IconUrl) && (request.IconUrl.Length > 1024 || !IsHttpUrl(request.IconUrl.Trim()) && !request.IconUrl.StartsWith('/')))
        {
            return "The icon must be a web address or a path on this server.";
        }

        provider.Type = request.Type;
        provider.Slug = slug;
        provider.DisplayName = displayName;
        provider.Enabled = request.Enabled;
        provider.IconUrl = string.IsNullOrWhiteSpace(request.IconUrl) ? null : request.IconUrl.Trim();
        provider.SortOrder = request.SortOrder;
        provider.Issuer = issuer;
        provider.ClientId = clientId;
        provider.Scopes = scopes;
        provider.EmailClaim = ClaimName(request.EmailClaim, "email");
        provider.NameClaim = ClaimName(request.NameClaim, "name");
        provider.GroupsClaim = ClaimName(request.GroupsClaim, "groups");
        provider.NewUserPolicy = request.NewUserPolicy;
        provider.LinkByEmail = request.LinkByEmail;
        provider.AllowedEmailDomains = (request.AllowedEmailDomains ?? [])
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();
        provider.RequiredGroup = string.IsNullOrWhiteSpace(request.RequiredGroup) ? null : request.RequiredGroup.Trim();
        provider.AdminGroup = string.IsNullOrWhiteSpace(request.AdminGroup) ? null : request.AdminGroup.Trim();

        if (request.ClearClientSecret)
        {
            provider.ClientSecretCiphertext = null;
        }
        else if (!string.IsNullOrEmpty(request.ClientSecret))
        {
            provider.ClientSecretCiphertext = secretProtector.Protect(request.ClientSecret);
        }

        return null;
    }

    private static string ClaimName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(uri.Host);
}
