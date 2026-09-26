using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Vora.Application.Devices;

// Where a device's address says it is. Private and reserved addresses are
// never sent to the lookup service: they have no location, and a home server's
// device list should not leave the house for nothing.
public static class ClientAddress
{
    public const string LocalNetwork = "Local Network";
    public const string UnknownLocation = "Unknown Location";

    // Free, no key, and HTTPS on the free tier. ip-api.com, used before, only
    // allows HTTPS with a paid key and refused every request - so every device
    // showed "Unknown Location", public addresses included.
    public static string LookupUrl(string ip) =>
        $"https://ipwho.is/{Uri.EscapeDataString(ip)}?fields=success,city,region,region_code,country_code";

    // Every private, loopback, link-local and shared range - not just
    // 192.168/10/127. Docker's own networks are 172.16.0.0/12, which the old
    // check missed, so a server in Docker sent its gateway address out to be
    // looked up and got nothing back.
    public static bool IsLocal(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] == 0;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    // "City, Region, CC" from an ipwho.is reply, or null when it has none.
    public static string? ParseLocation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

            var parts = new[] { Text(root, "city"), Text(root, "region_code") ?? Text(root, "region"), Text(root, "country_code") }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A device worth looking up again: never located, or last lookup failed.
    public static bool NeedsLookup(string? location) =>
        string.IsNullOrWhiteSpace(location) || location == UnknownLocation;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
