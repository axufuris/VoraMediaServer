using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Vora.Application.Auth.External;

public sealed record ExternalSignInGrant(Guid UserId, Guid ProviderId);

public interface IExternalSignInCodeStore
{
    string Issue(Guid userId, Guid providerId);
    ExternalSignInGrant? Redeem(string code);
}

public sealed class ExternalSignInCodeStore(TimeProvider clock) : IExternalSignInCodeStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private const int MaxOutstanding = 5000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(Guid UserId, Guid ProviderId, DateTimeOffset ExpiresAt);

    public string Issue(Guid userId, Guid providerId)
    {
        Purge();

        var code = Base64Url(RandomNumberGenerator.GetBytes(32));
        _entries[Hash(code)] = new Entry(userId, providerId, clock.GetUtcNow().Add(Lifetime));
        return code;
    }

    public ExternalSignInGrant? Redeem(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !_entries.TryRemove(Hash(code), out var entry))
        {
            return null;
        }

        return entry.ExpiresAt > clock.GetUtcNow() ? new ExternalSignInGrant(entry.UserId, entry.ProviderId) : null;
    }

    private void Purge()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        if (_entries.Count >= MaxOutstanding)
        {
            foreach (var key in _entries.OrderBy(pair => pair.Value.ExpiresAt).Take(_entries.Count - MaxOutstanding + 1).Select(pair => pair.Key).ToList())
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
