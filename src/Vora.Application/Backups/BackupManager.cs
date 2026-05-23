using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Backups.ViewModels;

namespace Vora.Application.Backups;

public sealed class BackupManagerOptions
{
    public string DefaultDirectory { get; set; } = "backups";
    public int SupportedSchemaVersion { get; set; } = 1;
}

public sealed class BackupManager : IBackupManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackupSettingsStore _settingsStore;
    private readonly BackupManagerOptions _options;
    private readonly ILogger<BackupManager> _logger;

    public BackupManager(
        IServiceScopeFactory scopeFactory,
        IBackupSettingsStore settingsStore,
        BackupManagerOptions options,
        ILogger<BackupManager> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsStore = settingsStore;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetEffectiveDirectoryAsync(CancellationToken ct = default)
    {
        var settings = await _settingsStore.GetAsync(ct);
        var dir = !string.IsNullOrWhiteSpace(settings.OverrideDirectory)
            ? settings.OverrideDirectory
            : _options.DefaultDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<List<AvailableSectionVM>> GetAvailableSectionsAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sections = scope.ServiceProvider.GetServices<IBackupSection>().ToList();
        return sections.Select(s => new AvailableSectionVM
        {
            Key = s.Key,
            DisplayName = s.DisplayName,
            Group = s.Group.ToString(),
            RequiresExplicitConfirm = s.RequiresExplicitConfirm,
            DestructiveWarning = s.DestructiveWarning
        }).ToList();
    }

    public async Task<BackupSummaryVM> CreateBackupAsync(string reason, CancellationToken ct = default)
    {
        var dir = await GetEffectiveDirectoryAsync(ct);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"vora-backup-{stamp}.zip";
        var fullPath = Path.Combine(dir, fileName);
        var tmpPath = fullPath + ".tmp";

        var manifest = new BackupManifest
        {
            SchemaVersion = _options.SupportedSchemaVersion,
            VoraServerVersion = GetServerVersion(),
            CreatedAtUtc = DateTime.UtcNow,
            Kind = "configuration",
            Reason = reason
        };

        await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new ZipBackupWriter(fs);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var sections = scope.ServiceProvider.GetServices<IBackupSection>().ToList();

            var configuredSettings = await _settingsStore.GetAsync(ct);
            if (configuredSettings.IncludedSectionKeys is { Count: > 0 } included)
            {
                var allow = included.ToHashSet(StringComparer.OrdinalIgnoreCase);
                sections = sections.Where(s => allow.Contains(s.Key)).ToList();
            }

            foreach (var section in sections)
            {
                ct.ThrowIfCancellationRequested();
                writer.BeginSection(section.Key);
                try
                {
                    await section.WriteAsync(writer, ct);
                    var size = await writer.GetSectionSizeAsync(ct);
                    manifest.Sections.Add(new BackupSectionManifestEntry
                    {
                        Key = section.Key,
                        DisplayName = section.DisplayName,
                        Group = section.Group.ToString(),
                        RequiresExplicitConfirm = section.RequiresExplicitConfirm,
                        DestructiveWarning = section.DestructiveWarning,
                        SizeBytes = size
                    });
                    manifest.TotalSizeBytes += size;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup section {Key} failed during write", section.Key);
                    throw new InvalidOperationException($"Backup section '{section.Key}' failed: {ex.Message}", ex);
                }
                finally
                {
                    writer.EndSection();
                }
            }

            await writer.WriteManifestAsync(manifest, ct);
        }

        await SelfTestAsync(tmpPath, ct);

        File.Move(tmpPath, fullPath, overwrite: true);

        var settings = await _settingsStore.GetAsync(ct);
        settings.LastSuccessfulRunUtc = DateTime.UtcNow;
        await _settingsStore.SaveAsync(settings, ct);

        await PruneAsync(dir, settings.MaxToKeep, ct);

        await using var notifyScope = _scopeFactory.CreateAsyncScope();
        var notifier = notifyScope.ServiceProvider.GetService<IClientNotifier>();
        if (notifier != null)
        {
            await notifier.NotifyBackupCreatedAsync(fileName);
        }

        return new BackupSummaryVM
        {
            FileName = fileName,
            CreatedAtUtc = manifest.CreatedAtUtc,
            FileSizeBytes = new FileInfo(fullPath).Length,
            SectionCount = manifest.Sections.Count,
            Reason = manifest.Reason,
            VoraServerVersion = manifest.VoraServerVersion,
            ManifestReadable = true
        };
    }

    public async Task<List<BackupSummaryVM>> ListBackupsAsync(CancellationToken ct = default)
    {
        var dir = await GetEffectiveDirectoryAsync(ct);
        var result = new List<BackupSummaryVM>();

        foreach (var file in Directory.EnumerateFiles(dir, "vora-backup-*.zip"))
        {
            ct.ThrowIfCancellationRequested();
            var fi = new FileInfo(file);
            var summary = new BackupSummaryVM
            {
                FileName = fi.Name,
                CreatedAtUtc = fi.CreationTimeUtc,
                FileSizeBytes = fi.Length,
                ManifestReadable = false
            };
            try
            {
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new ZipBackupReader(stream);
                var manifest = reader.ReadManifest();
                if (manifest != null)
                {
                    summary.CreatedAtUtc = manifest.CreatedAtUtc;
                    summary.SectionCount = manifest.Sections.Count;
                    summary.Reason = manifest.Reason;
                    summary.VoraServerVersion = manifest.VoraServerVersion;
                    summary.ManifestReadable = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse manifest for backup {File}", fi.Name);
            }
            result.Add(summary);
        }

        return result.OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    public async Task<BackupManifestVM?> GetManifestAsync(string fileName, CancellationToken ct = default)
    {
        var fullPath = await ResolveFileAsync(fileName, ct);
        if (fullPath == null) return null;

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new ZipBackupReader(stream);
        var manifest = reader.ReadManifest();
        if (manifest == null) return null;

        return new BackupManifestVM
        {
            FileName = fileName,
            SchemaVersion = manifest.SchemaVersion,
            VoraServerVersion = manifest.VoraServerVersion,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Kind = manifest.Kind,
            Reason = manifest.Reason,
            TotalSizeBytes = manifest.TotalSizeBytes,
            Sections = manifest.Sections.Select(s => new BackupSectionVM
            {
                Key = s.Key,
                DisplayName = s.DisplayName,
                Group = s.Group,
                RequiresExplicitConfirm = s.RequiresExplicitConfirm,
                DestructiveWarning = s.DestructiveWarning,
                SizeBytes = s.SizeBytes,
                ItemCount = s.ItemCount
            }).ToList()
        };
    }

    public async Task<RestoreBackupResult> RestoreBackupAsync(string fileName, RestoreBackupRequest request, Guid? currentAdminUserId, CancellationToken ct = default)
    {
        var fullPath = await ResolveFileAsync(fileName, ct);
        if (fullPath == null)
        {
            return new RestoreBackupResult { Success = false, Error = "Backup file not found." };
        }

        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new ZipBackupReader(fs);
        var manifest = reader.ReadManifest();
        if (manifest == null)
        {
            return new RestoreBackupResult { Success = false, Error = "Backup is missing a manifest and cannot be restored." };
        }

        if (manifest.SchemaVersion > _options.SupportedSchemaVersion)
        {
            return new RestoreBackupResult
            {
                Success = false,
                Error = $"Backup schema version {manifest.SchemaVersion} is newer than this server supports (version {_options.SupportedSchemaVersion}). Upgrade Vora before restoring."
            };
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var allSections = scope.ServiceProvider.GetServices<IBackupSection>().ToList();
        var selected = allSections
            .Where(s => request.SectionKeys.Contains(s.Key))
            .ToList();

        if (selected.Count == 0)
        {
            return new RestoreBackupResult { Success = false, Error = "No sections selected for restore." };
        }

        if (selected.Any(s => s.Key == "users.profiles") && currentAdminUserId.HasValue && !request.AcknowledgeAdminLoss)
        {
            var ok = await UserSnapshotContainsAsync(reader, currentAdminUserId.Value);
            if (!ok)
            {
                return new RestoreBackupResult
                {
                    Success = false,
                    Error = "This backup does not contain your admin account. Restoring would lock you out. Re-submit with AcknowledgeAdminLoss = true to proceed."
                };
            }
        }

        var result = new RestoreBackupResult { Success = true };

        var transactionFactory = scope.ServiceProvider.GetService<IBackupTransactionFactory>();
        IBackupTransaction? transaction = transactionFactory != null
            ? await transactionFactory.BeginAsync(ct)
            : null;

        try
        {
            foreach (var section in selected)
            {
                ct.ThrowIfCancellationRequested();
                reader.BeginSection(section.Key);
                try
                {
                    var importResult = await section.ReadAsync(reader, ct);
                    result.Sections.Add(new RestoreSectionResult
                    {
                        Key = section.Key,
                        Restored = true,
                        RowsImported = importResult.RowsImported,
                        RowsSkipped = importResult.RowsSkipped,
                        Warnings = importResult.Warnings
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Restore failed for section {Key} — rolling back the entire restore.", section.Key);
                    result.Success = false;
                    result.Sections.Add(new RestoreSectionResult
                    {
                        Key = section.Key,
                        Restored = false,
                        Error = ex.Message
                    });
                    foreach (var prior in result.Sections.Where(r => r.Restored))
                    {
                        prior.Restored = false;
                        prior.Warnings.Add("Rolled back because a later section failed.");
                    }
                    if (string.IsNullOrEmpty(result.Error))
                    {
                        result.Error = $"Section '{section.Key}' failed; the entire restore was rolled back.";
                    }
                    break;
                }
                finally
                {
                    reader.EndSection();
                }
            }

            if (transaction != null)
            {
                if (result.Success)
                {
                    await transaction.CommitAsync(ct);
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                }
            }
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }

        if (result.Success)
        {
            var notifier = scope.ServiceProvider.GetService<IClientNotifier>();
            if (notifier != null)
            {
                await notifier.NotifyBackupRestoredAsync(fileName, selected.Select(s => s.Key).ToList());
            }
        }

        return result;
    }

    public async Task DeleteBackupAsync(string fileName, CancellationToken ct = default)
    {
        var fullPath = await ResolveFileAsync(fileName, ct);
        if (fullPath != null)
        {
            File.Delete(fullPath);
        }
    }

    public async Task<Stream> OpenBackupStreamAsync(string fileName, CancellationToken ct = default)
    {
        var fullPath = await ResolveFileAsync(fileName, ct);
        if (fullPath == null) throw new FileNotFoundException(fileName);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public async Task<BackupSummaryVM> UploadBackupAsync(Stream input, string suggestedFileName, CancellationToken ct = default)
    {
        var safeName = SanitizeFileName(suggestedFileName);
        if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".zip";
        }
        var dir = await GetEffectiveDirectoryAsync(ct);
        var fullPath = Path.Combine(dir, safeName);

        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(fs, ct);
        }

        try
        {
            await SelfTestAsync(fullPath, ct);
        }
        catch
        {
            File.Delete(fullPath);
            throw;
        }

        var fi = new FileInfo(fullPath);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new ZipBackupReader(stream);
        var manifest = reader.ReadManifest();

        return new BackupSummaryVM
        {
            FileName = safeName,
            CreatedAtUtc = manifest?.CreatedAtUtc ?? fi.CreationTimeUtc,
            FileSizeBytes = fi.Length,
            SectionCount = manifest?.Sections.Count ?? 0,
            Reason = manifest?.Reason ?? "uploaded",
            VoraServerVersion = manifest?.VoraServerVersion,
            ManifestReadable = manifest != null
        };
    }

    private async Task<string?> ResolveFileAsync(string fileName, CancellationToken ct)
    {
        var safe = SanitizeFileName(fileName);
        var dir = await GetEffectiveDirectoryAsync(ct);
        var path = Path.Combine(dir, safe);
        return File.Exists(path) ? path : null;
    }

    private async Task SelfTestAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new ZipBackupReader(stream);
        var manifest = reader.ReadManifest();
        if (manifest == null)
        {
            throw new InvalidOperationException("Backup self-test failed: manifest missing.");
        }
    }

    private static async Task<bool> UserSnapshotContainsAsync(ZipBackupReader reader, Guid userId)
    {
        var users = await reader.ReadJsonAsync<List<Dictionary<string, object?>>>("users.profiles/users.json", CancellationToken.None);
        if (users == null) return false;
        var target = userId.ToString("D");
        foreach (var u in users)
        {
            if (u.TryGetValue("Id", out var idVal) && idVal != null && string.Equals(idVal.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task PruneAsync(string dir, int maxToKeep, CancellationToken ct)
    {
        if (maxToKeep <= 0) return;
        var files = Directory.EnumerateFiles(dir, "vora-backup-*.zip")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        foreach (var stale in files.Skip(maxToKeep))
        {
            ct.ThrowIfCancellationRequested();
            try { stale.Delete(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune old backup {File}", stale.Name);
            }
        }
        await Task.CompletedTask;
    }

    private static string SanitizeFileName(string name)
    {
        var trimmed = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(c, '_');
        }
        return trimmed;
    }

    private static string GetServerVersion()
    {
        var asm = Assembly.GetEntryAssembly();
        return asm?.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
