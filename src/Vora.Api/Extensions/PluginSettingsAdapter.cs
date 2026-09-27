using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Api.Extensions;

public class PluginSettingsAdapter(ISystemSettingsRepository settingsRepo) : IPluginSettingsProvider
{
    public Task<string?> GetSettingAsync(string pluginId, string key) =>
        settingsRepo.GetPluginSettingAsync(pluginId, key);

    public Task SetSettingAsync(string pluginId, string key, string value) =>
        settingsRepo.SetPluginSettingAsync(pluginId, key, value);

    public async Task<string> GetMetadataLanguageAsync()
    {
        var settings = await settingsRepo.GetSettingsAsync();
        return string.IsNullOrWhiteSpace(settings.MetadataLanguage) ? "eng" : settings.MetadataLanguage;
    }
}
