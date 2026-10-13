namespace Vora.Api.Extensions;

public static class ClientAddressExtensions
{
    private const string MappedIpv4Prefix = "::ffff:";

    public static string? GetClientAddress(this HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(address)) return null;

        return address.StartsWith(MappedIpv4Prefix, StringComparison.Ordinal) ? address[MappedIpv4Prefix.Length..] : address;
    }
}
