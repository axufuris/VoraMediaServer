using Vora.Domain.Enums;

namespace Vora.Application.Auth.External.Ldap;

public sealed record LdapConnectionSettings(
    string Host,
    int Port,
    LdapSecurityMode Security,
    string? CaCertificatePem,
    string? BindDn,
    string? BindPassword,
    string UserBaseDn,
    string UserFilter,
    string MailAttribute,
    string DisplayNameAttribute,
    string UniqueIdAttribute,
    string GroupAttribute,
    string? GroupBaseDn,
    string? GroupFilter);

public sealed record LdapUserEntry(string Dn, string? UniqueId, string? Mail, string? DisplayName, IReadOnlyList<string> Groups);

public enum LdapAuthStatus
{
    Authenticated,
    InvalidCredentials,
    Unreachable
}

public sealed record LdapAuthResult(LdapAuthStatus Status, LdapUserEntry? User, string? Detail)
{
    public static LdapAuthResult Authenticated(LdapUserEntry user) => new(LdapAuthStatus.Authenticated, user, null);
    public static LdapAuthResult InvalidCredentials(string detail) => new(LdapAuthStatus.InvalidCredentials, null, detail);
    public static LdapAuthResult Unreachable(string detail) => new(LdapAuthStatus.Unreachable, null, detail);
}

public sealed record LdapSampleResult(
    bool Connected,
    bool ServiceBound,
    int EntriesFound,
    bool SearchLimited,
    int WithMail,
    int WithoutMail,
    int WithUniqueId,
    IReadOnlyList<string> SampleDns,
    string? Detail);

public sealed record LdapClaimResult(ExternalClaims? Claims, bool MailMissing);

public interface ILdapDirectory
{
    Task<LdapAuthResult> AuthenticateAsync(LdapConnectionSettings settings, string username, string password, CancellationToken cancellationToken);
    Task<LdapSampleResult> SampleAsync(LdapConnectionSettings settings, CancellationToken cancellationToken);
}
