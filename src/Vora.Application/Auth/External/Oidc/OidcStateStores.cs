using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Vora.Application.Auth.External.Oidc;

public interface IOidcLoginStateStore
{
    string Create(OidcLoginState state);
    OidcLoginState? Take(string state);
}

public sealed class OidcLoginStateStore(TimeProvider clock) : IOidcLoginStateStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const int MaxOutstanding = 5000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(OidcLoginState State, DateTimeOffset ExpiresAt);

    public string Create(OidcLoginState state)
    {
        Purge();

        var key = Base64Url(RandomNumberGenerator.GetBytes(32));
        _entries[Hash(key)] = new Entry(state, clock.GetUtcNow().Add(Lifetime));
        return key;
    }

    public OidcLoginState? Take(string state)
    {
        if (string.IsNullOrWhiteSpace(state) || !_entries.TryRemove(Hash(state), out var entry))
        {
            return null;
        }

        return entry.ExpiresAt > clock.GetUtcNow() ? entry.State : null;
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

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public interface IExternalPendingStore
{
    string Create(ExternalPending pending);
    ExternalPending? Find(string handle);
    void Update(string handle, ExternalPending pending);
    void Remove(string handle);
}

public sealed class ExternalPendingStore(TimeProvider clock) : IExternalPendingStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxFailures = 5;

    private const int MaxOutstanding = 5000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(ExternalPending Pending, DateTimeOffset ExpiresAt);

    public string Create(ExternalPending pending)
    {
        Purge();

        var handle = Base64Url(RandomNumberGenerator.GetBytes(32));
        _entries[Hash(handle)] = new Entry(pending, clock.GetUtcNow().Add(Lifetime));
        return handle;
    }

    public ExternalPending? Find(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !_entries.TryGetValue(Hash(handle), out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= clock.GetUtcNow())
        {
            _entries.TryRemove(Hash(handle), out _);
            return null;
        }

        return entry.Pending;
    }

    public void Update(string handle, ExternalPending pending)
    {
        var key = Hash(handle);
        if (_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = entry with { Pending = pending };
        }
    }

    public void Remove(string handle)
    {
        if (!string.IsNullOrWhiteSpace(handle))
        {
            _entries.TryRemove(Hash(handle), out _);
        }
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

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
