namespace Vora.Application.Plugins.ViewModels;

public class PluginOptionVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // What this plugin calls itself in a result it returns — "Fanart.tv" where
    // Name is "Fanart.tv Music Artwork". A picker that narrows results by
    // provider has to match on this, not on Name, or nothing lines up.
    public string ProviderName { get; set; } = string.Empty;
    public string ExternalIdLabel { get; set; } = string.Empty;
    public string ExternalIdPlaceholder { get; set; } = string.Empty;
    public bool IsAiPlugin { get; set; }
    public IEnumerable<string> SupportedLibraryTypes { get; set; } = Array.Empty<string>();
}