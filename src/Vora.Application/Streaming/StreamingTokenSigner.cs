using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;

namespace Vora.Application.Streaming;

public interface IStreamingTokenSigner
{
    string Sign(string scope, string payload, TimeSpan ttl);
    bool TryVerify(string token, string expectedScope, out string payload);
}

public class StreamingTokenSigner : IStreamingTokenSigner
{
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<StreamingTokenSigner> _logger;

    public StreamingTokenSigner(IOptions<JwtOptions> jwtOptions, ILogger<StreamingTokenSigner> logger)
    {
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public string Sign(string scope, string payload, TimeSpan ttl)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var data = $"{scope}|{exp}|{payload}";
        var key = Encoding.UTF8.GetBytes(GetSecret());
        using var hmac = new HMACSHA256(key);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(data))}.{Base64UrlEncode(sig)}";
    }

    public bool TryVerify(string token, string expectedScope, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var dataBytes = Base64UrlDecode(parts[0]);
            var providedSig = Base64UrlDecode(parts[1]);
            var key = Encoding.UTF8.GetBytes(GetSecret());
            using var hmac = new HMACSHA256(key);
            var expectedSig = hmac.ComputeHash(dataBytes);

            if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig)) return false;

            var data = Encoding.UTF8.GetString(dataBytes);
            var firstSep = data.IndexOf('|');
            if (firstSep < 0) return false;
            var secondSep = data.IndexOf('|', firstSep + 1);
            if (secondSep < 0) return false;

            var scope = data[..firstSep];
            if (!string.Equals(scope, expectedScope, StringComparison.Ordinal)) return false;

            var expString = data.Substring(firstSep + 1, secondSep - firstSep - 1);
            if (!long.TryParse(expString, out var exp)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;

            payload = data[(secondSep + 1)..];
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Streaming token verification failed.");
            return false;
        }
    }

    private string GetSecret() => _jwtOptions.SecretKey ?? string.Empty;

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
