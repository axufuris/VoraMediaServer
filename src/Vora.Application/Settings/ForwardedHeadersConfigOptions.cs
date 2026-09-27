namespace Vora.Application.Settings;

public class ForwardedHeadersConfigOptions
{
    public const string SectionName = "ForwardedHeaders";

    // On by default: a reverse proxy in front of Vora is the usual setup, and
    // without this every client looked like the proxy. With no KnownProxies or
    // KnownNetworks set, any private-network peer is trusted to forward - see
    // ClientAddress.PrivateNetworks.
    public bool Enabled { get; set; } = true;

    public List<string> KnownProxies { get; set; } = new();

    public List<string> KnownNetworks { get; set; } = new();

    // How many proxies to walk back through. Only proxies that are themselves
    // trusted are walked past, so a larger limit can't be used to spoof: the
    // first untrusted address in the chain is the client.
    public int ForwardLimit { get; set; } = 5;
}
