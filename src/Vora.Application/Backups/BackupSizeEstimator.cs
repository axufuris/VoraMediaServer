using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Backups.ViewModels;

namespace Vora.Application.Backups;

public interface IBackupSizeEstimator
{
    Task<BackupSizeEstimateVM> EstimateAsync(bool refresh, CancellationToken ct = default);
    void Invalidate();
}

public sealed class BackupSizeEstimator : IBackupSizeEstimator
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupSizeEstimator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BackupSizeEstimateVM? _cached;

    public BackupSizeEstimator(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<BackupSizeEstimator> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BackupSizeEstimateVM> EstimateAsync(bool refresh, CancellationToken ct = default)
    {
        if (!refresh && IsFresh(_cached, out var cached)) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (!refresh && IsFresh(_cached, out cached)) return cached;

            var estimate = await ComputeAsync(ct);
            _cached = estimate;
            return estimate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cached = null;

    private bool IsFresh(BackupSizeEstimateVM? candidate, [NotNullWhen(true)] out BackupSizeEstimateVM? fresh)
    {
        fresh = candidate != null && _timeProvider.GetUtcNow().UtcDateTime - candidate.EstimatedAtUtc < CacheDuration
            ? candidate
            : null;
        return fresh != null;
    }

    private async Task<BackupSizeEstimateVM> ComputeAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sections = scope.ServiceProvider.GetServices<IBackupSection>().ToList();
        var writer = new CompressedSizeBackupWriter();
        var manifest = new BackupManifest { CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime };
        var estimate = new BackupSizeEstimateVM();

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            var sectionEstimate = new BackupSectionEstimateVM { Key = section.Key };
            writer.BeginSection(section.Key);
            try
            {
                await section.WriteAsync(writer, ct);
                sectionEstimate.EstimatedBytes = await writer.GetSectionSizeAsync(ct);
                sectionEstimate.RowCount = writer.GetSectionRowCount();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not estimate the size of backup section {Key}.", section.Key);
                sectionEstimate.Failed = true;
            }
            finally
            {
                writer.EndSection();
            }

            estimate.Sections.Add(sectionEstimate);
            manifest.Sections.Add(new BackupSectionManifestEntry
            {
                Key = section.Key,
                DisplayName = section.DisplayName,
                Group = section.Group.ToString(),
                RequiresExplicitConfirm = section.RequiresExplicitConfirm,
                DestructiveWarning = section.DestructiveWarning,
                SizeBytes = sectionEstimate.EstimatedBytes,
                ItemCount = sectionEstimate.RowCount
            });
        }

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BackupJson.ManifestOptions);
        estimate.OverheadBytes = await CompressedSizeBackupWriter.MeasureEntryAsync("manifest.json", manifestBytes, ct)
            + CompressedSizeBackupWriter.ZipEndOfArchiveBytes;
        estimate.EstimatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return estimate;
    }
}
