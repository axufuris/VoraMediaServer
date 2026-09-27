namespace Vora.Application.Plugins.ViewModels;

// Install and uninstall both answer with a sentence for the admin to read,
// because both need a server restart to take effect.
public class PluginActionResponse
{
    public required string Message { get; set; }
}
