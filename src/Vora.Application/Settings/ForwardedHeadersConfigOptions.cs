namespace Vora.Application.Settings;

public class ForwardedHeadersConfigOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; } = false;

    public List<string> KnownProxies { get; set; } = new();

    public List<string> KnownNetworks { get; set; } = new();

    public int ForwardLimit { get; set; } = 1;
}
