namespace Vora.Domain.Entities.Settings;

public class PluginSettingValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PluginId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
