using System.Security.Cryptography;
using System.Text;

namespace Vora.Application.Users;

public static class ProfilePin
{
    public static bool IsSet(string? pinHash) => !string.IsNullOrEmpty(pinHash);

    public static bool Verify(string? pinHash, string? pin)
    {
        if (!IsSet(pinHash)) return true;
        if (string.IsNullOrEmpty(pin)) return false;

        if (pinHash!.StartsWith("$2")) return BCrypt.Net.BCrypt.Verify(pin, pinHash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(pinHash),
            Encoding.UTF8.GetBytes(LegacyHash(pin)));
    }

    private static string LegacyHash(string pin) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
}
