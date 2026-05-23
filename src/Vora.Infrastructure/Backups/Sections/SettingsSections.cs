using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Settings;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class ServerSettingsBackupSection : EntityTableBackupSection<ServerSetting>
{
    public ServerSettingsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "settings.server";
    public override string DisplayName => "Server Settings";
    public override BackupSectionGroup Group => BackupSectionGroup.Settings;
    protected override DbSet<ServerSetting> Set(VoraDbContext db) => db.ServerSettings;
}

public sealed class PluginSettingsBackupSection : EntityTableBackupSection<PluginSettingValue>
{
    public PluginSettingsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "settings.plugins";
    public override string DisplayName => "Plugin Settings";
    public override BackupSectionGroup Group => BackupSectionGroup.Settings;
    protected override DbSet<PluginSettingValue> Set(VoraDbContext db) => db.PluginSettings;
}

public sealed class DataProtectionKeysBackupSection : IBackupSection
{
    private readonly string _keysDirectory;

    public DataProtectionKeysBackupSection(DataProtectionKeysBackupOptions options)
    {
        _keysDirectory = options.Directory;
    }

    public string Key => "settings.data-protection";
    public string DisplayName => "DataProtection Keys";
    public BackupSectionGroup Group => BackupSectionGroup.Security;
    public bool RequiresExplicitConfirm => true;
    public string? DestructiveWarning =>
        "Contains the keys that decrypt the saved SMTP password. Restoring replaces all keys on this server; existing encrypted values may no longer decrypt unless the matching keys are also imported.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        if (!Directory.Exists(_keysDirectory)) return;
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(_keysDirectory, "*.xml"))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            var bytes = await File.ReadAllBytesAsync(file, ct);
            await writer.WriteBytesAsync($"{Key}/{name}", bytes, ct);
            files.Add(name);
        }
        await writer.WriteJsonAsync($"{Key}/index.json", files, ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var index = await reader.ReadJsonAsync<List<string>>($"{Key}/index.json", ct);
        if (index == null) return new BackupSectionImportResult();
        Directory.CreateDirectory(_keysDirectory);

        var imported = 0;
        var warnings = new List<string>();
        foreach (var name in index)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await reader.ReadBytesAsync($"{Key}/{name}", ct);
            if (bytes == null)
            {
                warnings.Add($"Missing key file '{name}' in backup.");
                continue;
            }
            var target = Path.Combine(_keysDirectory, Path.GetFileName(name));
            await File.WriteAllBytesAsync(target, bytes, ct);
            imported++;
        }

        return new BackupSectionImportResult { RowsImported = imported, Warnings = warnings };
    }
}

public sealed class DataProtectionKeysBackupOptions
{
    public string Directory { get; set; } = string.Empty;
}
