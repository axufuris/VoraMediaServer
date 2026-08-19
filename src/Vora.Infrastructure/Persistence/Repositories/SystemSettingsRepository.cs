using Microsoft.EntityFrameworkCore;
using Vora.Application.Settings;
using Vora.Application.Settings.Dtos;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Entities.Settings;

namespace Vora.Infrastructure.Persistence.Repositories;

public class SystemSettingsRepository(VoraDbContext dbContext) : ISystemSettingsRepository
{
    private const int DefaultPublicPort = 32080;

    public async Task<ServerSetting> GetSettingsAsync()
    {
        var settings = await dbContext.ServerSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync();
        return settings ?? await GetSettingsForUpdateAsync();
    }

    public async Task<ServerSetting> GetSettingsForUpdateAsync()
    {
        var settings = await dbContext.ServerSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (settings != null)
        {
            return settings;
        }

        settings = new ServerSetting();
        await dbContext.ServerSettings.AddAsync(settings);
        await dbContext.SaveChangesAsync();
        return settings;
    }

    public Task SaveChangesAsync() => dbContext.SaveChangesAsync();

    public async Task<ServerSettingsVM> GetServerSettingsVMAsync()
    {
        var vm = await dbContext.ServerSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(ServerSettingsVM.Projection)
            .FirstOrDefaultAsync();

        if (vm != null)
        {
            return vm;
        }

        await GetSettingsForUpdateAsync();

        return await dbContext.ServerSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(ServerSettingsVM.Projection)
            .FirstAsync();
    }

    public async Task<RemoteAccessSettingsDto> GetRemoteAccessSettingsAsync()
    {
        var dto = await dbContext.ServerSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(RemoteAccessSettingsDto.Projection)
            .FirstOrDefaultAsync();

        return dto ?? new RemoteAccessSettingsDto
        {
            EnableRemoteAccess = true,
            ManuallySpecifyPublicPort = false,
            PublicPort = DefaultPublicPort
        };
    }

    public async Task<string?> GetPluginSettingAsync(string pluginId, string key)
    {
        var setting = await dbContext.PluginSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PluginId == pluginId && s.Key == key);
        return setting?.Value;
    }

    public Task<Dictionary<string, string>> GetAllPluginSettingsAsync(string pluginId) =>
        dbContext.PluginSettings
            .AsNoTracking()
            .Where(s => s.PluginId == pluginId)
            .ToDictionaryAsync(s => s.Key, s => s.Value);

    public async Task SetPluginSettingAsync(string pluginId, string key, string value)
    {
        var setting = await dbContext.PluginSettings
            .FirstOrDefaultAsync(s => s.PluginId == pluginId && s.Key == key);

        if (setting == null)
        {
            await dbContext.PluginSettings.AddAsync(new PluginSettingValue
            {
                PluginId = pluginId,
                Key = key,
                Value = value
            });
        }
        else
        {
            setting.Value = value;
        }

        await dbContext.SaveChangesAsync();
    }
}