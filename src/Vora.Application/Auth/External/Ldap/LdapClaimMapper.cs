using System.Text;

namespace Vora.Application.Auth.External.Ldap;

public static class LdapClaimMapper
{
    public static LdapClaimResult Map(LdapUserEntry entry)
    {
        var mail = entry.Mail?.Trim();
        if (string.IsNullOrEmpty(mail) || !mail.Contains('@'))
        {
            return new LdapClaimResult(null, MailMissing: true);
        }

        var subject = string.IsNullOrWhiteSpace(entry.UniqueId) ? entry.Dn.Trim().ToLowerInvariant() : entry.UniqueId.Trim();
        var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? null : entry.DisplayName.Trim();

        return new LdapClaimResult(new ExternalClaims(subject, mail, EmailVerified: true, name, Groups(entry.Groups)), MailMissing: false);
    }

    public static IReadOnlyList<string> Groups(IEnumerable<string> raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<string>();

        foreach (var value in raw)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                groups.Add(trimmed);
            }

            if (FirstRdnValue(trimmed) is { Length: > 0 } name && seen.Add(name))
            {
                groups.Add(name);
            }
        }

        return groups;
    }

    public static string? FirstRdnValue(string dn)
    {
        var equals = dn.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return null;
        }

        var value = new StringBuilder();
        for (var i = equals + 1; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                value.Append(dn[++i]);
                continue;
            }

            if (c is ',' or '+' or ';')
            {
                break;
            }

            value.Append(c);
        }

        return value.ToString().Trim();
    }
}
