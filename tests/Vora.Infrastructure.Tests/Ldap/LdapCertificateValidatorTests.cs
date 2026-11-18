using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vora.Infrastructure.Auth;

namespace Vora.Infrastructure.Tests.Ldap;

public class LdapCertificateValidatorTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private X509Certificate2 NewAuthority(string name, int startedDaysAgo = 1)
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-startedDaysAgo), DateTimeOffset.UtcNow.AddDays(30));
        _disposables.Add(certificate);
        return certificate;
    }

    private X509Certificate2 NewLeaf(X509Certificate2 authority, string host)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var signed = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), serial);
        var leaf = X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
        _disposables.Add(leaf);
        return leaf;
    }

    [Fact]
    public void Without_a_private_authority_only_a_clean_system_validation_is_trusted()
    {
        var authority = NewAuthority("Some CA");
        var leaf = NewLeaf(authority, "ldap.example.org");

        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.None, null).Should().BeTrue();
        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, null).Should().BeFalse();
        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, "  ").Should().BeFalse();
    }

    [Fact]
    public void A_certificate_signed_by_the_given_authority_is_trusted_even_though_the_system_does_not_know_it()
    {
        var authority = NewAuthority("Home CA");
        var leaf = NewLeaf(authority, "ldap.example.org");

        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, authority.ExportCertificatePem()).Should().BeTrue();
    }

    [Fact]
    public void A_certificate_signed_by_a_different_authority_is_refused()
    {
        var home = NewAuthority("Home CA");
        var other = NewAuthority("Other CA");
        var leaf = NewLeaf(other, "ldap.example.org");

        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, home.ExportCertificatePem()).Should().BeFalse();
    }

    [Fact]
    public void A_name_that_does_not_match_the_host_is_refused_even_with_the_right_authority()
    {
        var authority = NewAuthority("Home CA");
        var leaf = NewLeaf(authority, "other.example.org");

        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch, authority.ExportCertificatePem()).Should().BeFalse();
    }

    [Fact]
    public void A_missing_certificate_is_refused()
    {
        LdapCertificateValidator.IsTrusted(null, null, SslPolicyErrors.None, null).Should().BeFalse();

        var authority = NewAuthority("Home CA");
        var leaf = NewLeaf(authority, "ldap.example.org");
        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateNotAvailable, authority.ExportCertificatePem()).Should().BeFalse();
    }

    [Theory]
    [InlineData("not a certificate")]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")]
    public void An_authority_that_cannot_be_read_trusts_nothing(string pem)
    {
        var authority = NewAuthority("Home CA");
        var leaf = NewLeaf(authority, "ldap.example.org");

        LdapCertificateValidator.IsTrusted(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors, pem).Should().BeFalse();
    }

    [Fact]
    public void An_expired_certificate_is_refused_whoever_signed_it()
    {
        var authority = NewAuthority("Home CA", startedDaysAgo: 60);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ldap.example.org", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var signed = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-20), DateTimeOffset.UtcNow.AddDays(-10), serial);
        using var expired = X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);

        LdapCertificateValidator.IsTrusted(expired, null, SslPolicyErrors.RemoteCertificateChainErrors, authority.ExportCertificatePem()).Should().BeFalse();
    }
}
