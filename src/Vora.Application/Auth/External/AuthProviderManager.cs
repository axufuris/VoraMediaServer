using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vora.Application.Auth.External.Ldap;
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
    Task<AuthProviderType?> GetTypeAsync(Guid id);
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

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*(?:;[A-Za-z0-9-]+)?$")]
    private static partial Regex AttributePattern();

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

    public async Task<AuthProviderType?> GetTypeAsync(Guid id) =>
        (await providers.GetByIdAsync(id))?.Type;

    public async Task<AuthProvidersVM> GetPublicAsync()
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var enabled = await providers.GetEnabledAsync();
        return new AuthProvidersVM
        {
            PasswordSignInEnabled = settings.PasswordSignInEnabled,
            DirectorySignInEnabled = enabled.Any(p => p.Type == AuthProviderType.Ldap),
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

        if (!request.PasswordSignInEnabled && (await providers.GetEnabledAsync()).Count == 0)
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
        LdapHost = provider.LdapHost,
        LdapPort = provider.LdapPort,
        LdapSecurity = provider.LdapSecurity,
        LdapCaCertificatePem = provider.LdapCaCertificatePem,
        LdapBindDn = provider.LdapBindDn,
        HasBindPassword = !string.IsNullOrEmpty(provider.LdapBindPasswordCiphertext),
        LdapUserBaseDn = provider.LdapUserBaseDn,
        LdapUserFilter = provider.LdapUserFilter,
        LdapMailAttribute = provider.LdapMailAttribute,
        LdapDisplayNameAttribute = provider.LdapDisplayNameAttribute,
        LdapUniqueIdAttribute = provider.LdapUniqueIdAttribute,
        LdapGroupAttribute = provider.LdapGroupAttribute,
        LdapGroupBaseDn = provider.LdapGroupBaseDn,
        LdapGroupFilter = provider.LdapGroupFilter,
        CreatedAt = provider.CreatedAt,
        UpdatedAt = provider.UpdatedAt
    };

    private string? Apply(AuthProvider provider, SaveAuthProviderRequest request)
    {
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

        if (!string.IsNullOrWhiteSpace(request.IconUrl) && (request.IconUrl.Length > 1024 || !IsHttpUrl(request.IconUrl.Trim()) && !request.IconUrl.StartsWith('/')))
        {
            return "The icon must be a web address or a path on this server.";
        }

        var error = request.Type == AuthProviderType.Ldap ? ApplyLdap(provider, request) : ApplyOidc(provider, request);
        if (error is not null)
        {
            return error;
        }

        provider.Type = request.Type;
        provider.Slug = slug;
        provider.DisplayName = displayName;
        provider.Enabled = request.Enabled;
        provider.IconUrl = string.IsNullOrWhiteSpace(request.IconUrl) ? null : request.IconUrl.Trim();
        provider.SortOrder = request.SortOrder;
        provider.NewUserPolicy = request.NewUserPolicy;
        provider.LinkByEmail = request.LinkByEmail;
        provider.AllowedEmailDomains = (request.AllowedEmailDomains ?? [])
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();
        provider.RequiredGroup = string.IsNullOrWhiteSpace(request.RequiredGroup) ? null : request.RequiredGroup.Trim();
        provider.AdminGroup = string.IsNullOrWhiteSpace(request.AdminGroup) ? null : request.AdminGroup.Trim();

        return null;
    }

    private string? ApplyOidc(AuthProvider provider, SaveAuthProviderRequest request)
    {
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

        provider.Issuer = issuer;
        provider.ClientId = clientId;
        provider.Scopes = scopes;
        provider.EmailClaim = ClaimName(request.EmailClaim, "email");
        provider.NameClaim = ClaimName(request.NameClaim, "name");
        provider.GroupsClaim = ClaimName(request.GroupsClaim, "groups");

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

    private string? ApplyLdap(AuthProvider provider, SaveAuthProviderRequest request)
    {
        var host = (request.LdapHost ?? string.Empty).Trim();
        if (host.Length > 0 && !LdapHostRules.IsValidHost(host))
        {
            return "Enter the directory's host name or address by itself, without ldap:// or a port.";
        }

        if (request.LdapPort is { } port && (port < 1 || port > 65535))
        {
            return "The port must be between 1 and 65535.";
        }

        if (!Enum.IsDefined(request.LdapSecurity))
        {
            return "Pick how the connection is secured.";
        }

        if (host.Length > 0 && request.LdapSecurity == LdapSecurityMode.None && !LdapHostRules.IsPrivateNetworkHost(host))
        {
            return "An unencrypted connection sends every password in the clear, so it is allowed only to a directory on your own network. Use ldaps or StartTLS for this host.";
        }

        var userBaseDn = Clean(request.LdapUserBaseDn);
        if (userBaseDn is not null && (userBaseDn.Length > 512 || userBaseDn.Any(char.IsControl)))
        {
            return "The user search base is not valid.";
        }

        var userFilter = Clean(request.LdapUserFilter);
        if (userFilter is not null && LdapFilter.ValidateTemplate(userFilter, LdapFilter.UsernamePlaceholder) is { } filterError)
        {
            return $"User filter: {filterError}";
        }

        var groupBaseDn = Clean(request.LdapGroupBaseDn);
        var groupFilter = Clean(request.LdapGroupFilter);
        if (groupBaseDn is not null != groupFilter is not null)
        {
            return "Enter both the group search base and the group filter, or neither.";
        }

        if (groupBaseDn is not null && (groupBaseDn.Length > 512 || groupBaseDn.Any(char.IsControl)))
        {
            return "The group search base is not valid.";
        }

        if (groupFilter is not null && LdapFilter.ValidateTemplate(groupFilter, LdapFilter.DnPlaceholder, LdapFilter.UsernamePlaceholder) is { } groupError)
        {
            return $"Group filter: {groupError}";
        }

        var bindDn = Clean(request.LdapBindDn);
        if (bindDn is not null && (bindDn.Length > 512 || bindDn.Any(char.IsControl)))
        {
            return "The bind account is not valid.";
        }

        var mail = AttributeName(request.LdapMailAttribute, "mail");
        var displayNameAttribute = AttributeName(request.LdapDisplayNameAttribute, "displayName");
        var uniqueId = AttributeName(request.LdapUniqueIdAttribute, "entryUUID");
        var groupAttribute = AttributeName(request.LdapGroupAttribute, "memberOf");
        if (mail is null || displayNameAttribute is null || uniqueId is null || groupAttribute is null)
        {
            return "An attribute name may use letters, digits and hyphens, and must start with a letter.";
        }

        var caPem = Clean(request.LdapCaCertificatePem);
        if (caPem is not null && !IsCertificatePem(caPem))
        {
            return "The certificate authority is not a PEM certificate (the text between BEGIN CERTIFICATE and END CERTIFICATE).";
        }

        if (request.Enabled && (host.Length == 0 || userBaseDn is null || userFilter is null))
        {
            return "A directory needs its host, user search base and user filter before it can be turned on.";
        }

        provider.LdapHost = host.Length == 0 ? null : host;
        provider.LdapPort = request.LdapPort;
        provider.LdapSecurity = request.LdapSecurity;
        provider.LdapCaCertificatePem = caPem;
        provider.LdapBindDn = bindDn;
        provider.LdapUserBaseDn = userBaseDn;
        provider.LdapUserFilter = userFilter;
        provider.LdapMailAttribute = mail;
        provider.LdapDisplayNameAttribute = displayNameAttribute;
        provider.LdapUniqueIdAttribute = uniqueId;
        provider.LdapGroupAttribute = groupAttribute;
        provider.LdapGroupBaseDn = groupBaseDn;
        provider.LdapGroupFilter = groupFilter;

        if (request.ClearLdapBindPassword || bindDn is null)
        {
            provider.LdapBindPasswordCiphertext = null;
        }
        else if (!string.IsNullOrEmpty(request.LdapBindPassword))
        {
            provider.LdapBindPasswordCiphertext = secretProtector.Protect(request.LdapBindPassword);
        }

        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? AttributeName(string? value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return name.Length <= 64 && AttributePattern().IsMatch(name) ? name : null;
    }

    private static bool IsCertificatePem(string pem)
    {
        if (pem.Length > 32768)
        {
            return false;
        }

        try
        {
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPem(pem);
            return certificates.Count > 0;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string ClaimName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(uri.Host);
}
