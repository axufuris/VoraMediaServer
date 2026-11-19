using Microsoft.AspNetCore.DataProtection;
using Vora.Application.Webhooks;

namespace Vora.Infrastructure.Webhooks;

public class DataProtectionWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Purpose = "Vora.Webhooks.ConnectionSecret.v1";

    private readonly IDataProtector _protector;

    public DataProtectionWebhookSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
