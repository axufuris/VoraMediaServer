using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Vora.Application.Auth.External.Ldap;

public static partial class LdapHostRules
{
    private static readonly string[] PrivateSuffixes = [".local", ".lan", ".internal", ".home.arpa", ".localdomain"];

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$")]
    private static partial Regex HostnamePattern();

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
        {
            return false;
        }

        return IPAddress.TryParse(host, out _) || HostnamePattern().IsMatch(host);
    }

    public static bool IsPrivateNetworkHost(string host)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return IsPrivateAddress(address);
        }

        var lowered = host.ToLowerInvariant();
        return lowered == "localhost"
            || !lowered.Contains('.')
            || PrivateSuffixes.Any(suffix => lowered.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsPrivateAddress(address.MapToIPv4());
            }

            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}
