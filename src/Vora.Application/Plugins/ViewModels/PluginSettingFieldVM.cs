namespace Vora.Application.Plugins.ViewModels;

public class PluginSettingFieldVM
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
}