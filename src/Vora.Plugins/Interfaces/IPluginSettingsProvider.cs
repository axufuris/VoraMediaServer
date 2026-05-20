namespace Vora.Plugins.Interfaces;

public interface IPluginSettingsProvider
{
    Task<string?> GetSettingAsync(string pluginId, string key);
    Task SetSettingAsync(string pluginId, string key, string value);
}
