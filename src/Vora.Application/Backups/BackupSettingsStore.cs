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
        var backupSettings = Deserialize(settings.BackupConfigurationJson);
        BackupSectionSelection.UpgradeLegacyInclusionList(backupSettings);
        return backupSettings;
    }

    public async Task SaveAsync(BackupSettings settings, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var entity = await repo.GetSettingsForUpdateAsync();
        entity.BackupConfigurationJson = JsonSerializer.Serialize(settings);
        await repo.SaveChangesAsync();
    }

    public async Task<TimeZoneInfo> GetScheduleTimeZoneAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
        var settings = await repo.GetSettingsAsync();
        return ScheduleClock.Resolve(settings.ScheduleTimeZone);
    }

    private static BackupSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new BackupSettings();
        try
        {
            return JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();
        }
        catch (JsonException)
        {
            return new BackupSettings();
        }
    }
}
