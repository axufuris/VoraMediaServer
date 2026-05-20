namespace Vora.Plugins.Dtos;

public class PluginSettingDefinitionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
