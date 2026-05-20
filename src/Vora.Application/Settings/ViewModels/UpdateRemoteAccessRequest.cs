namespace Vora.Application.Settings.ViewModels;

public class UpdateRemoteAccessRequest
{
    public bool IsEnabled { get; set; }
    public bool ManuallySpecifyPort { get; set; }
    public int PublicPort { get; set; }
}
