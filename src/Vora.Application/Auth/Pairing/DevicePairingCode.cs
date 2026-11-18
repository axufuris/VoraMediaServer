using System.Security.Cryptography;

namespace Vora.Application.Auth.Pairing;

public static class DevicePairingCode
{
    public const string Alphabet = "BCDFGHJKMNPQRSTVWXZ23456789";
    public const int Length = 8;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    public static string Format(string code) => code.Length == Length ? $"{code[..4]}-{code[4..]}" : code;

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return cleaned.Length == Length && cleaned.All(c => Alphabet.Contains(c)) ? cleaned : null;
    }
}
