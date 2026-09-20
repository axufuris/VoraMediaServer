namespace Vora.Application.Settings.ViewModels;

public class RemoteAccessStatusVM
{
    public bool IsEnabled { get; set; }
    public bool UpnpSupported { get; set; }
    public string LocalIp { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string PublicIp { get; set; } = string.Empty;
    public int PublicPort { get; set; }
    public bool ManuallySpecifyPort { get; set; }
    public string? ExternalUrl { get; set; }
    // Whether the server actually responded to a request at its public endpoint
    // (the external URL, or the public IP:port), and which URL that probe hit.
    public bool Reachable { get; set; }
    public string AccessUrl { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
