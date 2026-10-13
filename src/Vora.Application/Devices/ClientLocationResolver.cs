using System.Collections.Concurrent;

namespace Vora.Application.Devices;

public interface IClientLocationResolver
{
    Task<string> ResolveAsync(string ip, string? fallback = null);
}

public sealed class ClientLocationResolver : IClientLocationResolver
{
    public const string HttpClientName = "GeoLookup";

    private const int MaxRemembered = 5_000;
    private static readonly TimeSpan RememberFor = TimeSpan.FromHours(12);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (string Location, DateTimeOffset At)> _recent = new(StringComparer.Ordinal);

    public ClientLocationResolver(IHttpClientFactory httpClientFactory, TimeProvider? time = null)
    {
        _httpClientFactory = httpClientFactory;
        _time = time ?? TimeProvider.System;
    }

    public async Task<string> ResolveAsync(string ip, string? fallback = null)
    {
        if (ClientAddress.IsLocal(ip)) return ClientAddress.LocalNetwork;
        if (ClientAddress.IsVpn(ip)) return ClientAddress.Vpn;

        if (_recent.TryGetValue(ip, out var known) && _time.GetUtcNow() - known.At < RememberFor)
        {
            return known.Location;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var location = ClientAddress.ParseLocation(await client.GetStringAsync(ClientAddress.LookupUrl(ip)));
            if (location != null)
            {
                if (_recent.Count >= MaxRemembered) _recent.Clear();
                _recent[ip] = (location, _time.GetUtcNow());
                return location;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }

        return fallback is { } kept && !ClientAddress.NeedsLookup(kept) ? kept : ClientAddress.UnknownLocation;
    }
}
