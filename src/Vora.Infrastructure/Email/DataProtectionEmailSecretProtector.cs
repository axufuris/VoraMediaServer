using Microsoft.AspNetCore.DataProtection;
using Vora.Application.Email;

namespace Vora.Infrastructure.Email;

public class DataProtectionEmailSecretProtector : IEmailSecretProtector
{
    private const string Purpose = "Vora.Email.SmtpPassword.v1";

    private readonly IDataProtector _protector;

    public DataProtectionEmailSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
