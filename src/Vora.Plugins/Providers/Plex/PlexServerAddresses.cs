using System.Net;
using System.Net.Sockets;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Plex;

public sealed record PlexServerAddress(string Uri, string Label);

public static class PlexServerAddresses
{
    public static IReadOnlyList<PlexServerAddress> InTryOrder(IEnumerable<RemoteConnectionDto> connections)
    {
        var all = connections.Where(c => !string.IsNullOrWhiteSpace(c.Uri)).ToList();
        var local = all.Where(c => c.IsLocal && !c.IsRelay).OrderByDescending(c => c.IsHttps).ToList();
        var remote = all.Where(c => !c.IsLocal && !c.IsRelay).OrderByDescending(c => c.IsHttps);

        var addresses = new List<PlexServerAddress>();
        addresses.AddRange(local.Select(c => new PlexServerAddress(c.Uri.TrimEnd('/'), $"the local address {Endpoint(c)}")));
        foreach (var connection in local)
        {
            if (PlainHttpUri(connection) is { } plain)
            {
                addresses.Add(new PlexServerAddress(plain, $"the local address {Endpoint(connection)} over plain HTTP"));
            }
        }
        addresses.AddRange(remote.Select(c => new PlexServerAddress(c.Uri.TrimEnd('/'), $"the remote address {Endpoint(c)}")));
        addresses.AddRange(all.Where(c => c.IsRelay).Select(c => new PlexServerAddress(c.Uri.TrimEnd('/'), "the Plex relay")));

        return addresses.DistinctBy(a => a.Uri, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? PlainHttpUri(RemoteConnectionDto connection) =>
        connection.IsHttps
        && connection.Address is { } address
        && IPAddress.TryParse(address, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetwork
        && connection.Port is int port
            ? $"http://{new IPEndPoint(ip, port)}"
            : null;

    private static string Endpoint(RemoteConnectionDto connection)
    {
        if (connection.Address is { } address && IPAddress.TryParse(address, out var ip) && connection.Port is int port)
        {
            return new IPEndPoint(ip, port).ToString();
        }
        return Uri.TryCreate(connection.Uri, UriKind.Absolute, out var uri) ? uri.Authority : connection.Uri;
    }
}
