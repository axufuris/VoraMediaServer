using Microsoft.AspNetCore.DataProtection;
using Vora.Application.Auth.External;

namespace Vora.Infrastructure.Auth;

public class DataProtectionAuthSecretProtector : IAuthSecretProtector
{
    private const string Purpose = "Vora.Auth.ProviderSecret.v1";

    private readonly IDataProtector _protector;

    public DataProtectionAuthSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
