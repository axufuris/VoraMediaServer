using Microsoft.AspNetCore.DataProtection;
using Vora.Application.Iptv;

namespace Vora.Infrastructure.Iptv;

public class DataProtectionIptvSecretProtector : IIptvSecretProtector
{
    public const string Purpose = "Vora.Iptv.ProviderPassword.v1";

    private readonly IDataProtector _protector;

    public DataProtectionIptvSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
