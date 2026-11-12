using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Vora.Application.Iptv;

namespace Vora.Infrastructure.Iptv;

public class DataProtectionPassthroughTokenProtector : IIptvPassthroughTokenProtector
{
    public const string Purpose = "Vora.Iptv.PassthroughToken.v1";

    private readonly ITimeLimitedDataProtector _protector;

    public DataProtectionPassthroughTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    public string Protect(string payload, DateTimeOffset expiresAt) => _protector.Protect(payload, expiresAt);

    public bool TryUnprotect(string token, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            payload = _protector.Unprotect(token);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
