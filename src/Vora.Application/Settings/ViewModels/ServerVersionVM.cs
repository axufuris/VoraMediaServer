namespace Vora.Application.Settings.ViewModels;

public class ServerVersionVM
{
    public string Version { get; set; } = string.Empty;
    public string? Commit { get; set; }
    public bool IsPrerelease { get; set; }
}
