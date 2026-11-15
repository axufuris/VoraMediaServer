using System.Net;
using System.Net.Sockets;

namespace Vora.Application.Podcasts;

// Which addresses the server may fetch for podcasts: a feed when it is
// subscribed or refreshed, and every hop of an episode's audio redirect chain
// (PodcastStreamResolver). Both come from feeds anyone allowed to add a custom
// feed can write, so neither may point the server at its own network.
internal static class PodcastAddressRules
{
    public static bool IsDisallowedAddress(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return true;
            if (b[0] == 10) return true;
            if (b[0] == 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            return false;
        }

        return true;
    }
}
