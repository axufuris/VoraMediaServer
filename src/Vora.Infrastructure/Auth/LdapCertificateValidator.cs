using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vora.Infrastructure.Auth;

public static class LdapCertificateValidator
{
    public static bool IsTrusted(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, string? caPem)
    {
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(caPem))
        {
            return errors == SslPolicyErrors.None;
        }

        var roots = new X509Certificate2Collection();
        try
        {
            roots.ImportFromPem(caPem);
        }
        catch (CryptographicException)
        {
            return false;
        }

        if (roots.Count == 0)
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(roots);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (chain is not null)
        {
            foreach (var element in chain.ChainElements.Skip(1))
            {
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        using var leaf = new X509Certificate2(certificate);
        return custom.Build(leaf);
    }
}
