using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Posters;
using Vora.Application.Recommendations;
using Vora.Application.Tasks.Dtos;
using Vora.Application.Tasks.ViewModels;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tasks;

public interface ITaskQueueManager
{
    void QueueLibraryAdded(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueLibraryUpdated(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueScanLibrary(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueRefreshLibraryMetadata(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueAnalyzeLibraryMediaContent(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false);
    void QueueScanMediaItem(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueScanNewFile(Guid libraryId, string filePath);
    void QueueRefreshMediaItemMetadata(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueAnalyzeMediaItemContent(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueArtworkProviderSwap(Guid libraryId, string libraryName);
    void QueueRefreshLibraryRatings(Guid libraryId, bool forceOverride = false);
    void QueueRefreshMediaItemArtwork(Guid mediaItemId, bool forceOverride = false);
    void QueueRefreshArtistArtwork(Guid artistId, string? artistName = null, bool forceOverride = false);
    void QueueRefreshAlbumArtwork(Guid albumId, string? albumName = null, bool forceOverride = false);
    void QueueRefreshAllActorMetadata();
    void QueueResolveTvdbIds();
    void QueueRemoveOrphanedMedia(string filePath);
    void QueueCollectionChronologySync(Guid collectionId, string title);
    void QueueCollectionContentSync(Guid collectionId, string title);
    void QueueGeneratePosterOverlays(Guid mediaItemId);
    void QueueFullCollectionSync(Guid collectionId, string title, bool hasContentSync, bool hasChronologySort);
    void QueueReevaluateCollectionOrder(Guid collectionId);
    Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem, Func<IServiceProvider, Task<string?>>? nameResolver = null);
    bool CancelTask(Guid taskId);
    CancellationToken? GetTaskCancellationToken(Guid taskId);
    void UpdateTaskName(Guid taskId, string name);
    Func<IServiceProvider, Task<string?>>? GetTaskNameResolver(Guid taskId);
    IAsyncEnumerable<QueuedTaskDto> DequeueAsync(CancellationToken cancellationToken);
    void MarkTaskAsRunning(Guid taskId);
    void ReportProgress(string? detail);
    void RemoveTask(Guid taskId);
    IEnumerable<QueuedTaskVM> GetAllTasks();
    void QueueGenerateAiEmbeddings();
    void QueueGenerateLibraryPosterOverlays(Guid libraryId, string? libraryName = null);
    void QueueIptvEpgSync();
    void QueueGenerateLibraryVideoThumbnails(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false);
    void QueueGenerateMediaItemVideoThumbnails(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
}

public class TaskQueueManager : ITaskQueueManager
{
    private const int AiEmbeddingsBatchSize = 100;
    private const string RunningStatus = "Running";
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromMilliseconds(500);

    private readonly IClientNotifier _notifier;
    private readonly Channel<QueuedTaskDto> _queue = Channel.CreateUnbounded<QueuedTaskDto>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _taskTokens = new();
    private readonly ConcurrentDictionary<Guid, QueuedTaskDto> _taskStates = new();

    private Guid? _runningTaskId;
    private DateTime _lastProgressNotifyUtc = DateTime.MinValue;

    public TaskQueueManager(IClientNotifier notifier)
    {
        _notifier = notifier;
    }

    public void QueueLibraryAdded(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Auto-Ingest Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: true, ct),
            libraryName == null ? LibraryLabel(libraryId, "Auto-Ingest Library: {0}") : null);
    }

    public void QueueLibraryUpdated(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Update Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: false, ct),
            libraryName == null ? LibraryLabel(libraryId, "Update Library: {0}") : null);
    }

    public void QueueScanLibrary(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Scan Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: false, ct),
            libraryName == null ? LibraryLabel(libraryId, "Scan Library: {0}") : null);
    }

    public void QueueRefreshLibraryMetadata(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Metadata for Library: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerLibraryMetadataRefreshAsync(libraryId, forceOverride: forceOverride);
            await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride: forceOverride);
            await metadataManager.TriggerLibraryRatingsRefreshAsync(libraryId, forceOverride: forceOverride);
            await metadataManager.TriggerActorMetadataRefreshAsync();

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId);
        });
    }

    public void QueueAnalyzeLibraryMediaContent(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        EnqueueTask($"Analyze Library Media: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            await analyzerManager.TriggerLibrarySilenceDetectionAsync(libraryId, libraryName, forceOverride: forceOverride, isScheduleTrigger: isScheduleTrigger);
        });
    }

    public void QueueScanMediaItem(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Scan Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var mediaManager = sp.GetRequiredService<IMediaManager>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await mediaManager.TriggerTargetedScanAsync(mediaItemId);
            await analyzerManager.TriggerMediaItemFileAnalysisAsync(mediaItemId, mediaItemName);

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerActorMetadataRefreshAsync();

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId);

            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, mediaItemName, forceOverride: forceOverride, isAdditionTrigger: true);
        });
    }

    public void QueueScanNewFile(Guid libraryId, string filePath)
    {
        EnqueueTask($"Scan File: {Path.GetFileName(filePath)}", async (ct, sp) =>
        {
            var libraryManager = sp.GetRequiredService<ILibraryManager>();
            var result = await libraryManager.TriggerFileScanAsync(libraryId, filePath);
            if (result.MediaItemId == null) return;
            var itemId = result.MediaItemId.Value;

            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            ct.ThrowIfCancellationRequested();
            await analyzerManager.TriggerMediaItemFileAnalysisAsync(itemId, null);

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(itemId, false);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(itemId, false);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(itemId, false);

            // A brand-new season's poster/episode-count come from the parent
            // show's mapping — refresh the show ONCE, only when a new season was
            // created, so a season-folder copy doesn't trigger a metadata flood.
            if (result.NewSeasonCreated && result.ParentShowId.HasValue)
            {
                await metadataManager.TriggerMediaItemMetadataRefreshAsync(result.ParentShowId.Value, false);
            }

            // Actor entity metadata (bios, photos) is NOT refreshed per file:
            // TriggerActorMetadataRefreshAsync fetches up to 50 actors from TMDB
            // each call, which is crippling when a whole library is ingested one
            // file at a time. The item's own cast is already linked by the
            // metadata refresh above; actor entities are enriched by the nightly
            // scan and the full-library workflow.
            await overlayManager.GenerateOverlaysForMediaAsync(itemId);
            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(itemId, null, isAdditionTrigger: true);
        });
    }

    public void QueueRefreshMediaItemMetadata(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Metadata for Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(mediaItemId, forceOverride);
            await metadataManager.TriggerActorMetadataRefreshAsync();

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId);
        });
    }

    public void QueueAnalyzeMediaItemContent(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Analyze Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, mediaItemName, forceOverride: forceOverride);
        });
    }

    public void QueueArtworkProviderSwap(Guid libraryId, string libraryName)
    {
        EnqueueTask($"Provider Swap Artwork Sync: {libraryName}", async (ct, sp) =>
        {
            var artworkRepo = sp.GetRequiredService<IMediaArtworkRepository>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await artworkRepo.ClearArtworkForLibraryAsync(libraryId);
            await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride: true);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId);
        });
    }

    public void QueueRefreshLibraryRatings(Guid libraryId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Ratings for Library: {libraryId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerLibraryRatingsRefreshAsync(libraryId, null, forceOverride);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId);
        }, LibraryLabel(libraryId, "Refresh Ratings for Library: {0}"));
    }

    public void QueueRefreshMediaItemArtwork(Guid mediaItemId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Artwork for Media Item: {mediaItemId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride);

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId);
        }, MediaLabel(mediaItemId, "Refresh Artwork for Media Item: {0}"));
    }

    public void QueueRefreshArtistArtwork(Guid artistId, string? artistName = null, bool forceOverride = false)
    {
        var label = !string.IsNullOrWhiteSpace(artistName) ? artistName : artistId.ToString();
        EnqueueTask($"Refresh Artwork for Artist: {label}", async (ct, sp) =>
        {
            var musicManager = sp.GetRequiredService<IMusicManager>();
            await musicManager.RefreshArtistArtworkFromProvidersAsync(artistId, forceOverride, ct);
        });
    }

    public void QueueRefreshAlbumArtwork(Guid albumId, string? albumName = null, bool forceOverride = false)
    {
        var label = !string.IsNullOrWhiteSpace(albumName) ? albumName : albumId.ToString();
        EnqueueTask($"Refresh Artwork for Album: {label}", async (ct, sp) =>
        {
            var musicManager = sp.GetRequiredService<IMusicManager>();
            await musicManager.RefreshAlbumArtworkFromProvidersAsync(albumId, forceOverride, ct);
        });
    }

    public void QueueRefreshAllActorMetadata()
    {
        EnqueueTask("Refresh All Actor Metadata", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            await metadataManager.TriggerActorMetadataRefreshAsync();
        });
    }

    public void QueueResolveTvdbIds()
    {
        EnqueueTask("Resolve TVDB Ids", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            await metadataManager.TriggerMediaTvdbResolutionAsync();
        });
    }

    public void QueueRemoveOrphanedMedia(string filePath)
    {
        EnqueueTask($"Auto-Cleanup: {Path.GetFileName(filePath)}", async (ct, sp) =>
        {
            var mediaRepo = sp.GetRequiredService<IMediaRepository>();
            await mediaRepo.MarkMediaMissingByFilePathAsync(filePath);
        });
    }

    public void QueueCollectionChronologySync(Guid collectionId, string title)
    {
        EnqueueTask($"Chronological Collection Sync: {title}", async (ct, sp) =>
        {
            var orderingService = sp.GetRequiredService<CollectionOrderingService>();
            await orderingService.ApplyChronologicalOrderAsync(collectionId);
        });
    }

    public void QueueCollectionContentSync(Guid collectionId, string title)
    {
        EnqueueTask($"Content Sync: {title}", async (ct, sp) =>
        {
            var syncService = sp.GetRequiredService<CollectionSyncService>();
            await syncService.SyncCollectionContentAsync(collectionId);
        });
    }

    public void QueueGeneratePosterOverlays(Guid mediaItemId)
    {
        EnqueueTask($"Generate Poster Overlays: {mediaItemId}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.GenerateOverlaysForMediaAsync(mediaItemId);

            var notifier = sp.GetRequiredService<IClientNotifier>();
            await notifier.NotifyMediaItemUpdatedAsync(mediaItemId);
        }, MediaLabel(mediaItemId, "Generate Poster Overlays: {0}"));
    }

    public void QueueFullCollectionSync(Guid collectionId, string title, bool hasContentSync, bool hasChronologySort)
    {
        EnqueueTask($"Sync Collection: {title}", async (ct, sp) =>
        {
            if (hasContentSync)
            {
                var syncService = sp.GetRequiredService<CollectionSyncService>();
                await syncService.SyncCollectionContentAsync(collectionId);
            }

            if (hasChronologySort)
            {
                var orderingService = sp.GetRequiredService<CollectionOrderingService>();
                await orderingService.ApplyChronologicalOrderAsync(collectionId);
            }
        });
    }

    public void QueueReevaluateCollectionOrder(Guid collectionId)
    {
        EnqueueTask("Reevaluate Collection Order", async (ct, sp) =>
        {
            var orderingService = sp.GetRequiredService<CollectionOrderingService>();
            await orderingService.ReevaluateOrderOnItemAddedAsync(collectionId, ct);
        });
    }

    public Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem, Func<IServiceProvider, Task<string?>>? nameResolver = null)
    {
        var task = new QueuedTaskDto { Name = name, WorkItem = workItem, NameResolver = nameResolver };
        var cts = new CancellationTokenSource();

        _taskTokens.TryAdd(task.Id, cts);
        _taskStates.TryAdd(task.Id, task);

        _queue.Writer.TryWrite(task);

        _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());

        return task.Id;
    }

    public Func<IServiceProvider, Task<string?>>? GetTaskNameResolver(Guid taskId)
    {
        return _taskStates.TryGetValue(taskId, out var task) ? task.NameResolver : null;
    }

    public void UpdateTaskName(Guid taskId, string name)
    {
        if (_taskStates.TryGetValue(taskId, out var state) && state.Name != name)
        {
            state.Name = name;
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    private static Func<IServiceProvider, Task<string?>> LibraryLabel(Guid libraryId, string format) =>
        async sp =>
        {
            var repo = sp.GetRequiredService<ILibraryRepository>();
            var name = await repo.GetProjectedByIdAsync(libraryId, l => l.Name);
            return string.IsNullOrWhiteSpace(name) ? null : string.Format(format, name);
        };

    private static Func<IServiceProvider, Task<string?>> MediaLabel(Guid mediaItemId, string format) =>
        async sp =>
        {
            var repo = sp.GetRequiredService<IMediaRepository>();
            var title = await repo.GetProjectedAsync(mediaItemId, m => m.Title);
            return string.IsNullOrWhiteSpace(title) ? null : string.Format(format, title);
        };

    public void MarkTaskAsRunning(Guid taskId)
    {
        if (_taskStates.TryGetValue(taskId, out var state))
        {
            state.Status = RunningStatus;
            _runningTaskId = taskId;
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public void ReportProgress(string? detail)
    {
        var taskId = _runningTaskId;
        if (taskId == null || !_taskStates.TryGetValue(taskId.Value, out var state)) return;
        if (state.Progress == detail) return;

        state.Progress = detail;

        var now = DateTime.UtcNow;
        if (detail == null || now - _lastProgressNotifyUtc >= ProgressNotifyInterval)
        {
            _lastProgressNotifyUtc = now;
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public bool CancelTask(Guid taskId)
    {
        if (_taskTokens.TryGetValue(taskId, out var cts))
        {
            // Keep the entry so a still-queued task is observed as cancelled
            // (and skipped) by the worker, and a running task's linked token —
            // built from this same source — fires. RemoveTask disposes it.
            cts.Cancel();
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
            return true;
        }
        return false;
    }

    public CancellationToken? GetTaskCancellationToken(Guid taskId)
    {
        return _taskTokens.TryGetValue(taskId, out var cts) ? cts.Token : (CancellationToken?)null;
    }

    public IAsyncEnumerable<QueuedTaskDto> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }

    public void RemoveTask(Guid taskId)
    {
        if (_runningTaskId == taskId) _runningTaskId = null;
        if (_taskTokens.TryRemove(taskId, out var cts)) cts.Dispose();
        if (_taskStates.TryRemove(taskId, out _))
        {
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public IEnumerable<QueuedTaskVM> GetAllTasks()
    {
        return _taskStates.Values
            .OrderByDescending(t => t.Status == RunningStatus)
            .Select(t => new QueuedTaskVM
            {
                Id = t.Id,
                Name = t.Name,
                Status = t.Status,
                Progress = t.Progress
            })
            .ToList();
    }

    public void QueueGenerateAiEmbeddings()
    {
        EnqueueTask("Generate AI Embeddings", async (ct, sp) =>
        {
            var embeddingService = sp.GetRequiredService<IMediaEmbeddingService>();
            int processed;

            do
            {
                processed = await embeddingService.ProcessMissingEmbeddingsAsync(AiEmbeddingsBatchSize);
            } while (processed == AiEmbeddingsBatchSize && !ct.IsCancellationRequested);
        });
    }

    public void QueueGenerateLibraryPosterOverlays(Guid libraryId, string? libraryName = null)
    {
        var label = string.IsNullOrWhiteSpace(libraryName)
            ? "Generate Library Poster Overlays"
            : $"Generate Library Poster Overlays: {libraryName}";

        EnqueueTask(label, async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.RunLibraryOverlaySyncAsync(libraryId, ct);
        }, LibraryLabel(libraryId, "Generate Library Poster Overlays: {0}"));
    }

    public void QueueIptvEpgSync()
    {
        EnqueueTask("IPTV EPG Sync", async (ct, sp) =>
        {
            var epgService = sp.GetRequiredService<IIptvEpgService>();
            var dvrManager = sp.GetRequiredService<IDvrManager>();

            await epgService.SyncEpgDataAsync(ct);

            await dvrManager.ProcessSchedulesIntoSessionsAsync();
        });
    }

    public void QueueGenerateLibraryVideoThumbnails(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        EnqueueTask($"Generate Video Thumbnails: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
            await manager.TriggerLibraryThumbnailGenerationAsync(libraryId, forceOverride: forceOverride, isScheduleTrigger: isScheduleTrigger);
        });
    }

    public void QueueGenerateMediaItemVideoThumbnails(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Generate Video Thumbnails: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
            await manager.TriggerMediaItemThumbnailGenerationAsync(mediaItemId, forceOverride: forceOverride);
        });
    }

    private static async Task RunFullLibraryWorkflowAsync(IServiceProvider sp, Guid libraryId, string? libraryName, bool forceOverride, bool isAdditionTrigger, CancellationToken ct = default)
    {
        var metadataManager = sp.GetRequiredService<IMetadataManager>();
        var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
        var libraryManager = sp.GetRequiredService<ILibraryManager>();
        var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();
        var progress = sp.GetRequiredService<ITaskProgressReporter>();

        // The individual trigger methods don't yet take a token, so honour
        // cancellation between the (long) steps — a cancel stops the workflow
        // at the next boundary instead of running to completion.
        // Scan + enrich each show/movie together as an isolated unit, several
        // at a time. Each unit scans and enriches in the SAME DbContext (so the
        // posters land on the rows the scan just created — nothing to clobber),
        // and different units run in parallel for speed. Posters therefore fill
        // in per show/movie as the scan progresses, not after it finishes.
        ct.ThrowIfCancellationRequested();
        progress.Report("Scanning & loading…");

        var libraryVm = await libraryManager.GetLibraryByIdAsync(libraryId);
        var libraryType = libraryVm?.Type switch
        {
            "Movie" => (LibraryType?)LibraryType.Movie,
            "TvShow" => LibraryType.TvShow,
            _ => null
        };

        var units = libraryType.HasValue
            ? await libraryManager.DiscoverScanUnitsAsync(libraryId)
            : new List<ScanUnit>();

        if (units.Count > 0 && libraryType.HasValue)
        {
            var total = units.Count;
            var done = 0;
            var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
            await Parallel.ForEachAsync(
                units,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
                async (unit, unitCt) =>
                {
                    progress.Report($"Scanning & loading {CleanUnitLabel(unit.Label)}…");
                    try
                    {
                        await libraryManager.ScanAndEnrichUnitAsync(libraryId, libraryType.Value, unit.FilePaths, forceOverride);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* one failing show/movie shouldn't abort the whole library */ }

                    var n = Interlocked.Increment(ref done);
                    progress.Report($"Scanning & loading {CleanUnitLabel(unit.Label)}… ({n}/{total})");
                });
        }
        else
        {
            // Music (or nothing discovered): whole-library scan then enrich.
            await libraryManager.TriggerLibraryFolderAndFileScanAsync(libraryId);
            await metadataManager.TriggerLibraryEnrichmentAsync(libraryId, forceOverride: false);
        }

        // Safety net for anything the per-unit path missed (never double-fetches
        // an already-enriched item; force stays off here so a force rescan the
        // units already handled isn't re-run).
        ct.ThrowIfCancellationRequested();
        progress.Report("Fetching details…");
        await metadataManager.TriggerLibraryEnrichmentAsync(libraryId, forceOverride: false);

        ct.ThrowIfCancellationRequested();
        progress.Report("Generating poster overlays…");
        await overlayManager.RunLibraryOverlaySyncAsync(libraryId);

        ct.ThrowIfCancellationRequested();
        progress.Report("Refreshing actor metadata…");
        await metadataManager.TriggerActorMetadataRefreshAsync();

        // Analysis + marker detection are heavy FFmpeg passes that don't affect
        // posters, so they run last — after the library is visually populated.
        ct.ThrowIfCancellationRequested();
        progress.Report("Analyzing media…");
        await analyzerManager.TriggerLibraryFileAnalysisAsync(libraryId, libraryName);

        ct.ThrowIfCancellationRequested();
        progress.Report("Detecting intro/credit markers…");
        await analyzerManager.TriggerLibrarySilenceDetectionAsync(libraryId, forceOverride: forceOverride, isAdditionTrigger: isAdditionTrigger);

        progress.Report(null);
    }

    private static string ResolveDisplayName(Guid id, string? name) =>
        string.IsNullOrEmpty(name) ? id.ToString() : name;

    private static string CleanUnitLabel(string label)
    {
        // Strip the trailing external-id tag (e.g. " [imdb-tt0115082]") so the
        // task shows "3rd Rock from the Sun (1996)" instead of the raw folder.
        var idx = label.IndexOf(" [", StringComparison.Ordinal);
        return idx > 0 ? label[..idx].Trim() : label.Trim();
    }
}
