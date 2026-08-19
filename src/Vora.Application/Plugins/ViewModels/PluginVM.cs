namespace Vora.Application.Plugins.ViewModels;

public class PluginVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemPlugin { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? DeveloperName { get; set; }
    public string? LatestVersionApiUrl { get; set; }
    public string? DocumentationUrl { get; set; }
    public string? ExternalConfigurationHint { get; set; }
    public bool HasSettings { get; set; }
    public bool IsAiPlugin { get; set; }
    public bool IsEnabled { get; set; }
    public bool RequiresConfiguration { get; set; }
}