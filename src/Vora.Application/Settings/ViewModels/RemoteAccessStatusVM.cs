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
    public string ErrorMessage { get; set; } = string.Empty;
}
