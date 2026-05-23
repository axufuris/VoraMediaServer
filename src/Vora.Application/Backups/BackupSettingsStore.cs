using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Settings;

namespace Vora.Application.Backups;

public sealed class BackupSettingsStore : IBackupSettingsStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BackupSettingsStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<BackupSettings> GetAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var settings = await repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BackupConfigurationJson))
        {
            return new BackupSettings();
        }
        try
        {
            return JsonSerializer.Deserialize<BackupSettings>(settings.BackupConfigurationJson) ?? new BackupSettings();
        }
        catch
        {
            return new BackupSettings();
        }
    }

    public async Task SaveAsync(BackupSettings settings, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var entity = await repo.GetSettingsForUpdateAsync();
        entity.BackupConfigurationJson = JsonSerializer.Serialize(settings);
        await repo.SaveChangesAsync();
    }
}
