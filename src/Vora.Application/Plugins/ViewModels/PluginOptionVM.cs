namespace Vora.Application.Plugins.ViewModels;

public class PluginOptionVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExternalIdLabel { get; set; } = string.Empty;
    public string ExternalIdPlaceholder { get; set; } = string.Empty;
    public bool IsAiPlugin { get; set; }
    public IEnumerable<string> SupportedLibraryTypes { get; set; } = Array.Empty<string>();
}