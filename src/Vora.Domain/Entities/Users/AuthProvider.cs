using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Users;

public class AuthProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AuthProviderType Type { get; set; } = AuthProviderType.Oidc;
    public required string Slug { get; set; }
    public required string DisplayName { get; set; }
    public bool Enabled { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }

    public string? Issuer { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecretCiphertext { get; set; }
    public string Scopes { get; set; } = "openid email profile";

    public string EmailClaim { get; set; } = "email";
    public string NameClaim { get; set; } = "name";
    public string GroupsClaim { get; set; } = "groups";

    public AuthNewUserPolicy NewUserPolicy { get; set; } = AuthNewUserPolicy.FollowRegistrationMode;
    public AuthLinkByEmailPolicy LinkByEmail { get; set; } = AuthLinkByEmailPolicy.ConfirmWithPassword;
    public List<string> AllowedEmailDomains { get; set; } = new();
    public string? RequiredGroup { get; set; }
    public string? AdminGroup { get; set; }

    public string? LdapHost { get; set; }
    public int? LdapPort { get; set; }
    public LdapSecurityMode LdapSecurity { get; set; } = LdapSecurityMode.Ldaps;
    public string? LdapCaCertificatePem { get; set; }
    public string? LdapBindDn { get; set; }
    public string? LdapBindPasswordCiphertext { get; set; }
    public string? LdapUserBaseDn { get; set; }
    public string? LdapUserFilter { get; set; }
    public string LdapMailAttribute { get; set; } = "mail";
    public string LdapDisplayNameAttribute { get; set; } = "displayName";
    public string LdapUniqueIdAttribute { get; set; } = "entryUUID";
    public string LdapGroupAttribute { get; set; } = "memberOf";
    public string? LdapGroupBaseDn { get; set; }
    public string? LdapGroupFilter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<UserExternalIdentity> Identities { get; set; } = new List<UserExternalIdentity>();
}
