namespace Vora.Plugins.Interfaces;

public interface IPluginSettingsProvider
{
    Task<string?> GetSettingAsync(string pluginId, string key);
    Task SetSettingAsync(string pluginId, string key, string value);

    // Server-wide metadata language (TVDB 3-letter code, e.g. "eng"). Metadata
    // providers read this to request titles/overviews in the admin's language.
    Task<string> GetMetadataLanguageAsync();
}
