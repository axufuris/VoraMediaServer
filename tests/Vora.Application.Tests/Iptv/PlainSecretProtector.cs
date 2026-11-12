using System.Security.Cryptography;
using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public sealed class PlainSecretProtector : IIptvSecretProtector
{
    private const string Prefix = "sealed:";

    public string Protect(string plaintext) => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string ciphertext)
    {
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("The key ring changed.");
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[Prefix.Length..]));
    }
}
