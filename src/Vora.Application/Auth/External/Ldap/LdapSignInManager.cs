using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Vora.Application.Auth.External.Oidc;
using Vora.Application.Auth.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth.External.Ldap;

public enum LdapSignInOutcome
{
    InvalidCredentials,
    SignedIn,
    Pending,
    Refused
}

public sealed record LdapSignInResult(LdapSignInOutcome Outcome, Guid? UserId, string? PendingHandle, ExternalSignInOutcome? PendingKind, string? Message)
{
    public static LdapSignInResult Invalid { get; } = new(LdapSignInOutcome.InvalidCredentials, null, null, null, null);
    public static LdapSignInResult SignedIn(Guid userId) => new(LdapSignInOutcome.SignedIn, userId, null, null, null);
    public static LdapSignInResult Pending(string handle, ExternalSignInOutcome kind) => new(LdapSignInOutcome.Pending, null, handle, kind, null);
    public static LdapSignInResult Refused(string message) => new(LdapSignInOutcome.Refused, null, null, null, message);
}

public interface ILdapSignInManager
{
    Task<bool> AnyEnabledAsync();
    Task<LdapSignInResult> SignInAsync(string identifier, string password, CancellationToken cancellationToken);
    Task<LdapTestResultVM?> TestAsync(Guid providerId, CancellationToken cancellationToken);
}

public sealed class LdapSignInManager(
    IAuthProviderRepository providers,
    IAuthSecretProtector secrets,
    ILdapDirectory directory,
    IExternalSignInService signIn,
    IExternalPendingStore pendings,
    ILogger<LdapSignInManager> logger) : ILdapSignInManager
{
    public const string MailMissingMessage = "Your directory entry has no email address, which Vora needs to make your account. Ask your administrator to add one.";

    public async Task<bool> AnyEnabledAsync() =>
        (await providers.GetEnabledAsync()).Any(p => p.Type == AuthProviderType.Ldap);

    public async Task<LdapSignInResult> SignInAsync(string identifier, string password, CancellationToken cancellationToken)
    {
        if (!LdapFilter.IsValidUsername(identifier) || string.IsNullOrEmpty(password))
        {
            return LdapSignInResult.Invalid;
        }

        var directories = (await providers.GetEnabledAsync())
            .Where(p => p.Type == AuthProviderType.Ldap)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.DisplayName)
            .ToList();

        foreach (var provider in directories)
        {
            var settings = LdapSettingsFactory.Create(provider, secrets, logger);
            if (settings is null)
            {
                continue;
            }

            var attempt = await directory.AuthenticateAsync(settings, identifier, password, cancellationToken);
            if (attempt.Status == LdapAuthStatus.Unreachable)
            {
                logger.LogWarning("The directory {Provider} could not be used for a sign-in: {Reason}", provider.Slug, attempt.Detail);
                continue;
            }

            if (attempt.Status != LdapAuthStatus.Authenticated || attempt.User is null)
            {
                logger.LogInformation("The directory {Provider} did not accept a sign-in: {Reason}", provider.Slug, attempt.Detail);
                continue;
            }

            return await CompleteAsync(provider, attempt.User, cancellationToken);
        }

        return LdapSignInResult.Invalid;
    }

    public async Task<LdapTestResultVM?> TestAsync(Guid providerId, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(providerId);
        if (provider is null || provider.Type != AuthProviderType.Ldap)
        {
            return null;
        }

        var result = new LdapTestResultVM();
        var settings = LdapSettingsFactory.Create(provider, secrets, logger);
        if (settings is null)
        {
            result.Message = "Enter the host, the user search base and the user filter first, and save the bind password again if the server keys changed.";
            return result;
        }

        var sample = await directory.SampleAsync(settings, cancellationToken);
        result.Connected = sample.Connected;
        result.ServiceBound = sample.ServiceBound;
        result.EntriesFound = sample.EntriesFound;
        result.SearchLimited = sample.SearchLimited;
        result.EntriesWithMail = sample.WithMail;
        result.EntriesWithoutMail = sample.WithoutMail;
        result.EntriesWithUniqueId = sample.WithUniqueId;
        result.SampleDns = sample.SampleDns.ToList();

        if (!sample.Connected || !sample.ServiceBound)
        {
            result.Message = sample.Detail ?? "The directory could not be reached.";
            return result;
        }

        result.Ok = sample.EntriesFound > 0;
        result.Message = sample.EntriesFound == 0
            ? "The directory answered but the user search found nobody. Check the search base and the filter."
            : $"The directory answered and the user search found {(sample.SearchLimited ? "at least " : string.Empty)}{sample.EntriesFound} entr{(sample.EntriesFound == 1 ? "y" : "ies")}.";

        if (provider.LdapSecurity == LdapSecurityMode.None)
        {
            result.Warnings.Add("The connection is not encrypted, so the bind password and every sign-in password cross the network in the clear. Use ldaps or StartTLS unless this is a network you trust.");
        }

        if (string.IsNullOrWhiteSpace(provider.LdapBindDn))
        {
            result.Warnings.Add("No bind account is set, so the directory is searched anonymously. Most directories refuse that.");
        }

        if (sample.WithoutMail > 0)
        {
            result.Warnings.Add($"{sample.WithoutMail} of the {sample.WithMail + sample.WithoutMail} entries checked have no value in the mail attribute ({settings.MailAttribute}). They cannot sign in, because Vora needs an email address for every account.");
        }

        if (sample.EntriesFound > 0 && sample.WithMail == 0)
        {
            result.Ok = false;
            result.Message = $"The user search works, but none of the entries checked has the mail attribute ({settings.MailAttribute}). Check the attribute name.";
        }

        if (sample.EntriesFound > 0 && sample.WithUniqueId == 0)
        {
            result.Warnings.Add($"None of the entries checked has the unique id attribute ({settings.UniqueIdAttribute}), so sign-ins are tied to the entry's distinguished name, which changes if the entry is moved or renamed.");
        }

        return result;
    }

    private async Task<LdapSignInResult> CompleteAsync(AuthProvider provider, LdapUserEntry user, CancellationToken cancellationToken)
    {
        var mapped = LdapClaimMapper.Map(user);
        if (mapped.Claims is null)
        {
            logger.LogInformation("A directory entry from {Provider} has no usable email address, so the sign-in was refused", provider.Slug);
            return LdapSignInResult.Refused(MailMissingMessage);
        }

        var result = await signIn.SignInAsync(provider.Id, mapped.Claims, new ExternalSignInOptions(), cancellationToken);

        switch (result.Outcome)
        {
            case ExternalSignInOutcome.SignedIn when result.UserId is Guid userId:
                return LdapSignInResult.SignedIn(userId);

            case ExternalSignInOutcome.Refused:
                return LdapSignInResult.Refused(result.Message ?? "This sign-in is not allowed.");

            case ExternalSignInOutcome.SignedIn:
                return LdapSignInResult.Refused("This sign-in could not be completed.");

            default:
                var handle = pendings.Create(new ExternalPending(provider.Id, provider.DisplayName, mapped.Claims, null, null, result, 0));
                return LdapSignInResult.Pending(handle, result.Outcome);
        }
    }
}

public static class LdapSettingsFactory
{
    public const int DefaultLdapsPort = 636;
    public const int DefaultLdapPort = 389;

    public static LdapConnectionSettings? Create(AuthProvider provider, IAuthSecretProtector secrets, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(provider.LdapHost) || string.IsNullOrWhiteSpace(provider.LdapUserBaseDn) || string.IsNullOrWhiteSpace(provider.LdapUserFilter))
        {
            return null;
        }

        string? bindPassword = null;
        if (!string.IsNullOrEmpty(provider.LdapBindPasswordCiphertext))
        {
            try
            {
                bindPassword = secrets.Unprotect(provider.LdapBindPasswordCiphertext);
            }
            catch (CryptographicException)
            {
                logger.LogWarning("The saved bind password of {Provider} cannot be read with this server's keys; enter it again", provider.Slug);
                return null;
            }
        }

        var port = provider.LdapPort ?? (provider.LdapSecurity == LdapSecurityMode.Ldaps ? DefaultLdapsPort : DefaultLdapPort);

        return new LdapConnectionSettings(
            provider.LdapHost.Trim(),
            port,
            provider.LdapSecurity,
            string.IsNullOrWhiteSpace(provider.LdapCaCertificatePem) ? null : provider.LdapCaCertificatePem,
            string.IsNullOrWhiteSpace(provider.LdapBindDn) ? null : provider.LdapBindDn.Trim(),
            bindPassword,
            provider.LdapUserBaseDn.Trim(),
            provider.LdapUserFilter.Trim(),
            provider.LdapMailAttribute,
            provider.LdapDisplayNameAttribute,
            provider.LdapUniqueIdAttribute,
            provider.LdapGroupAttribute,
            string.IsNullOrWhiteSpace(provider.LdapGroupBaseDn) ? null : provider.LdapGroupBaseDn.Trim(),
            string.IsNullOrWhiteSpace(provider.LdapGroupFilter) ? null : provider.LdapGroupFilter.Trim());
    }
}
