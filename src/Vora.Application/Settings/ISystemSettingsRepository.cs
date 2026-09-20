using Vora.Application.Settings.Dtos;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Settings;

public interface ISystemSettingsRepository
{
    Task<ServerSetting> GetSettingsAsync();
    Task<ServerSetting> GetSettingsForUpdateAsync();
    Task SaveChangesAsync();
    Task<ServerSettingsVM> GetServerSettingsVMAsync();
    Task<RemoteAccessSettingsDto> GetRemoteAccessSettingsAsync();
    Task<string?> GetPluginSettingAsync(string pluginId, string key);
    Task SetPluginSettingAsync(string pluginId, string key, string value);
    Task<Dictionary<string, string>> GetAllPluginSettingsAsync(string pluginId);
}
