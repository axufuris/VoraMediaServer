using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    void QueueDeleteLibrary(Guid libraryId, string? libraryName = null);
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
    void QueueMergeDuplicateShows();
    void QueueRemoveOrphanedMedia(string filePath);
    void QueueCollectionChronologySync(Guid collectionId, string title);
    void QueueCollectionContentSync(Guid collectionId, string title);
    void QueueGeneratePosterOverlays(Guid mediaItemId);
    void QueueFullCollectionSync(Guid collectionId, string title, bool hasContentSync, bool hasChronologySort);
    void QueueReevaluateCollectionOrder(Guid collectionId);
    Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem, Func<IServiceProvider, Task<string?>>? nameResolver = null, string? resourceKey = null, string? dedupeKey = null);
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
    void QueueOverlayOrphanSweep();
    void QueueIptvEpgSync();
    void QueueIptvHealthCheck(Guid playlistId, string? playlistName = null);
    void QueueGenerateLibraryVideoThumbnails(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false);
    void QueueGenerateMediaItemVideoThumbnails(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
}

public class TaskQueueManager : ITaskQueueManager
{
    private const int AiEmbeddingsBatchSize = 100;
    private const string PendingStatus = "Pending";
    private const string RunningStatus = "Running";
    private const string CancellingStatus = "Cancelling";
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromMilliseconds(500);

    private readonly IClientNotifier _notifier;
    private readonly Channel<QueuedTaskDto> _queue = Channel.CreateUnbounded<QueuedTaskDto>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _taskTokens = new();
    private readonly ConcurrentDictionary<Guid, QueuedTaskDto> _taskStates = new();

    // The task whose work is running on THIS async flow. AsyncLocal (not a
    // single field) because the scheduler runs several tasks concurrently — a
    // shared _runningTaskId let one task's progress overwrite another's and, once
    // the other finished and nulled it, froze the survivor's label entirely.
    private static readonly AsyncLocal<Guid?> _currentTaskId = new();
    private DateTime _lastProgressNotifyUtc = DateTime.MinValue;

    public TaskQueueManager(IClientNotifier notifier)
    {
        _notifier = notifier;
    }

    public void QueueLibraryAdded(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Auto-Ingest Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, ct),
            libraryName == null ? LibraryLabel(libraryId, "Auto-Ingest Library: {0}") : null,
            resourceKey: LibraryKey(libraryId));
    }

    public void QueueLibraryUpdated(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Update Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, ct),
            libraryName == null ? LibraryLabel(libraryId, "Update Library: {0}") : null,
            resourceKey: LibraryKey(libraryId));
    }

    public void QueueScanLibrary(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Scan Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, ct),
            libraryName == null ? LibraryLabel(libraryId, "Scan Library: {0}") : null,
            resourceKey: LibraryKey(libraryId));
    }

    public void QueueDeleteLibrary(Guid libraryId, string? libraryName = null)
    {
        EnqueueTask($"Delete Library: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
            {
                var manager = sp.GetRequiredService<Vora.Application.Libraries.ILibraryManager>();
                await manager.DeleteLibraryAsync(libraryId, ct);
            },
            libraryName == null ? LibraryLabel(libraryId, "Delete Library: {0}") : null,
            resourceKey: LibraryKey(libraryId));
    }

    public void QueueRefreshLibraryMetadata(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Metadata for Library: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerLibraryMetadataRefreshAsync(libraryId, forceOverride: forceOverride, cancellationToken: ct);
            await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride: forceOverride, cancellationToken: ct);
            await metadataManager.TriggerLibraryRatingsRefreshAsync(libraryId, forceOverride: forceOverride, cancellationToken: ct);
            await metadataManager.TriggerActorMetadataRefreshAsync(ct);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId, ct);
        }, resourceKey: LibraryKey(libraryId));
    }

    public void QueueAnalyzeLibraryMediaContent(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        EnqueueTask($"Analyze Library Media: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            await analyzerManager.TriggerLibraryFileAnalysisAsync(libraryId, libraryName, ct);
            await analyzerManager.TriggerLibrarySilenceDetectionAsync(libraryId, libraryName, forceOverride: forceOverride, isScheduleTrigger: isScheduleTrigger, cancellationToken: ct);
        },
        libraryName == null ? LibraryLabel(libraryId, "Analyze Library Media: {0}") : null,
        resourceKey: LibraryMaintenanceKey(libraryId));
    }

    public void QueueScanMediaItem(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Scan Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var mediaManager = sp.GetRequiredService<IMediaManager>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await mediaManager.TriggerTargetedScanAsync(mediaItemId, ct);
            await analyzerManager.TriggerMediaItemFileAnalysisAsync(mediaItemId, mediaItemName, ct);

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerActorMetadataRefreshAsync(ct);

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId, ct);

            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, mediaItemName, forceOverride: forceOverride, isAdditionTrigger: true, cancellationToken: ct);
        });
    }

    public void QueueScanNewFile(Guid libraryId, string filePath)
    {
        EnqueueTask($"Scan File: {Path.GetFileName(filePath)}", async (ct, sp) =>
        {
            var libraryManager = sp.GetRequiredService<ILibraryManager>();
            var result = await libraryManager.TriggerFileScanAsync(libraryId, filePath, ct);
            if (result.MediaItemId == null) return;
            var itemId = result.MediaItemId.Value;

            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            ct.ThrowIfCancellationRequested();
            await analyzerManager.TriggerMediaItemFileAnalysisAsync(itemId, null, ct);

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(itemId, false, ct);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(itemId, false, ct);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(itemId, false, ct);

            // A brand-new season's poster/episode-count come from the parent
            // show's mapping — refresh the show ONCE, only when a new season was
            // created, so a season-folder copy doesn't trigger a metadata flood.
            if (result.NewSeasonCreated && result.ParentShowId.HasValue)
            {
                await metadataManager.TriggerMediaItemMetadataRefreshAsync(result.ParentShowId.Value, false, ct);
            }

            // Actor entity metadata (bios, photos) is NOT refreshed per file:
            // TriggerActorMetadataRefreshAsync fetches up to 50 actors from TMDB
            // each call, which is crippling when a whole library is ingested one
            // file at a time. The item's own cast is already linked by the
            // metadata refresh above; actor entities are enriched by the nightly
            // scan and the full-library workflow.
            await overlayManager.GenerateOverlaysForMediaAsync(itemId, ct);
            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(itemId, null, isAdditionTrigger: true, cancellationToken: ct);

            var collectionMembership = sp.GetRequiredService<CollectionMembershipService>();
            await collectionMembership.CheckMediaItemForCollectionsAsync(itemId, ct);
        }, resourceKey: LibraryKey(libraryId));
    }

    public void QueueRefreshMediaItemMetadata(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Metadata for Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerMediaItemMetadataRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerMediaItemRatingsRefreshAsync(mediaItemId, forceOverride, ct);
            await metadataManager.TriggerActorMetadataRefreshAsync(ct);

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId, ct);
        });
    }

    public void QueueAnalyzeMediaItemContent(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Analyze Media Item: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, mediaItemName, forceOverride: forceOverride, cancellationToken: ct);
        }, mediaItemName == null ? MediaLabel(mediaItemId, "Analyze Media Item: {0}") : null);
    }

    public void QueueArtworkProviderSwap(Guid libraryId, string libraryName)
    {
        EnqueueTask($"Provider Swap Artwork Sync: {libraryName}", async (ct, sp) =>
        {
            var artworkRepo = sp.GetRequiredService<IMediaArtworkRepository>();
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await artworkRepo.ClearArtworkForLibraryAsync(libraryId);
            await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride: true, cancellationToken: ct);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId, ct);
        });
    }

    public void QueueRefreshLibraryRatings(Guid libraryId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Ratings for Library: {libraryId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerLibraryRatingsRefreshAsync(libraryId, null, forceOverride, ct);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId, ct);
        }, LibraryLabel(libraryId, "Refresh Ratings for Library: {0}"));
    }

    public void QueueRefreshMediaItemArtwork(Guid mediaItemId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Artwork for Media Item: {mediaItemId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride, ct);

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId, ct);
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
            await metadataManager.TriggerActorMetadataRefreshAsync(ct);
        });
    }

    public void QueueResolveTvdbIds()
    {
        EnqueueTask("Resolve TVDB Ids", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            await metadataManager.TriggerMediaTvdbResolutionAsync(ct);
        });
    }

    public void QueueMergeDuplicateShows()
    {
        EnqueueTask("Merge Duplicate TV Shows", async (ct, sp) =>
        {
            var dedupeManager = sp.GetRequiredService<Vora.Application.Media.IMediaDedupeManager>();
            await dedupeManager.MergeDuplicateTvShowsAsync(cancellationToken: ct);
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
            await orderingService.ApplyChronologicalOrderAsync(collectionId, cancellationToken: ct);
        }, resourceKey: CollectionKey(collectionId));
    }

    public void QueueCollectionContentSync(Guid collectionId, string title)
    {
        EnqueueTask($"Content Sync: {title}", async (ct, sp) =>
        {
            var syncService = sp.GetRequiredService<CollectionSyncService>();
            await syncService.SyncCollectionContentAsync(collectionId, ct);
        }, resourceKey: CollectionKey(collectionId));
    }

    public void QueueGeneratePosterOverlays(Guid mediaItemId)
    {
        EnqueueTask($"Generate Poster Overlays: {mediaItemId}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.GenerateOverlaysForMediaAsync(mediaItemId, ct);

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
                await syncService.SyncCollectionContentAsync(collectionId, ct);
            }

            if (hasChronologySort)
            {
                var orderingService = sp.GetRequiredService<CollectionOrderingService>();
                await orderingService.ApplyChronologicalOrderAsync(collectionId, cancellationToken: ct);
            }
        }, resourceKey: CollectionKey(collectionId));
    }

    public void QueueReevaluateCollectionOrder(Guid collectionId)
    {
        EnqueueTask("Reevaluate Collection Order", async (ct, sp) =>
        {
            var orderingService = sp.GetRequiredService<CollectionOrderingService>();
            await orderingService.ReevaluateOrderOnItemAddedAsync(collectionId, ct);
        }, resourceKey: CollectionKey(collectionId));
    }

    public Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem, Func<IServiceProvider, Task<string?>>? nameResolver = null, string? resourceKey = null, string? dedupeKey = null)
    {
        // Don't enqueue a duplicate of an operation that's already queued or
        // running (e.g. the daily thumbnail schedule firing over a manual run).
        // Best-effort: the states dict holds only active tasks, and a schedule vs.
        // manual trigger are far enough apart that a tight race isn't a concern.
        if (dedupeKey != null)
        {
            var existing = _taskStates.Values.FirstOrDefault(t => t.DedupeKey == dedupeKey);
            if (existing != null) return existing.Id;
        }

        var task = new QueuedTaskDto { Name = name, WorkItem = workItem, NameResolver = nameResolver, ResourceKey = resourceKey ?? Guid.NewGuid().ToString(), DedupeKey = dedupeKey };
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
            // Runs on the worker's per-task async flow (right before awaiting the
            // work item), so this pins progress reporting to THIS task for the
            // duration of its work — even while other tasks run concurrently.
            _currentTaskId.Value = taskId;
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public void ReportProgress(string? detail)
    {
        var taskId = _currentTaskId.Value;
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
            if (_taskStates.TryGetValue(taskId, out var state))
            {
                // A task that never started running has no in-flight work to wind
                // down, and the worker won't revisit it until its resource key frees
                // — which can be hours behind a long-running same-key task, leaving
                // it stuck "Cancelling". Remove it now; the worker skips it (its
                // token is gone) if it's ever dequeued.
                if (state.Status == PendingStatus)
                {
                    RemoveTask(taskId);
                    return true;
                }
                // A running task keeps a transient "Cancelling" state so the UI
                // shows feedback until the work actually stops (a running FFmpeg
                // pass, say). RemoveTask drops the entry when it stops.
                state.Status = CancellingStatus;
            }
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
        // No _runningTaskId to clear — _currentTaskId is AsyncLocal and lives only
        // on the finished task's own async flow, so it goes away with it.
        if (_taskTokens.TryRemove(taskId, out var cts)) cts.Dispose();
        if (_taskStates.TryRemove(taskId, out _))
        {
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public IEnumerable<QueuedTaskVM> GetAllTasks()
    {
        return _taskStates.Values
            .OrderByDescending(t => t.Status == RunningStatus || t.Status == CancellingStatus)
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
        string label;
        Func<IServiceProvider, Task<string?>>? nameResolver = null;

        if (libraryId == Guid.Empty)
        {
            label = "Generate Poster Overlays: All Libraries";
        }
        else if (!string.IsNullOrWhiteSpace(libraryName))
        {
            label = $"Generate Poster Overlays: {libraryName}";
        }
        else
        {
            label = "Generate Poster Overlays";
            nameResolver = LibraryLabel(libraryId, "Generate Poster Overlays: {0}");
        }

        EnqueueTask(label, async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.RunLibraryOverlaySyncAsync(libraryId, ct);
        }, nameResolver, resourceKey: OverlaySyncKey);
    }

    public void QueueOverlayOrphanSweep()
    {
        EnqueueTask("Sweep Orphaned Poster Overlays", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.SweepOrphanedOverlayFilesAsync(ct);
        }, resourceKey: OverlaySyncKey);
    }

    public void QueueIptvEpgSync()
    {
        EnqueueTask("IPTV EPG Sync", async (ct, sp) =>
        {
            var epgService = sp.GetRequiredService<IIptvEpgService>();
            var dvrManager = sp.GetRequiredService<IDvrManager>();

            await epgService.SyncEpgDataAsync(ct);

            await dvrManager.ProcessSchedulesIntoSessionsAsync(ct);
        });
    }

    public void QueueIptvHealthCheck(Guid playlistId, string? playlistName = null)
    {
        var label = string.IsNullOrWhiteSpace(playlistName)
            ? "Health-check IPTV channels"
            : $"Health-check channels: {playlistName}";

        EnqueueTask(label, async (ct, sp) =>
        {
            var service = sp.GetRequiredService<Vora.Application.Iptv.IIptvHealthCheckService>();
            await service.CheckPlaylistAsync(playlistId, ct);
        }, resourceKey: $"iptv-health:{playlistId}");
    }

    public void QueueGenerateLibraryVideoThumbnails(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false)
    {
        EnqueueTask($"Generate Video Thumbnails: {ResolveDisplayName(libraryId, libraryName)}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
            await manager.TriggerLibraryThumbnailGenerationAsync(libraryId, forceOverride: forceOverride, isScheduleTrigger: isScheduleTrigger, cancellationToken: ct);
        }, resourceKey: LibraryMaintenanceKey(libraryId), dedupeKey: $"gen-thumbs:{libraryId}:{forceOverride}");
    }

    public void QueueGenerateMediaItemVideoThumbnails(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false)
    {
        EnqueueTask($"Generate Video Thumbnails: {ResolveDisplayName(mediaItemId, mediaItemName)}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<Vora.Application.Thumbnails.IVideoThumbnailManager>();
            await manager.TriggerMediaItemThumbnailGenerationAsync(mediaItemId, forceOverride: forceOverride, cancellationToken: ct);
        });
    }

    private static async Task RunFullLibraryWorkflowAsync(IServiceProvider sp, Guid libraryId, string? libraryName, bool forceOverride, CancellationToken ct = default)
    {
        var metadataManager = sp.GetRequiredService<IMetadataManager>();
        var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
        var libraryManager = sp.GetRequiredService<ILibraryManager>();
        var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();
        var dedupeManager = sp.GetRequiredService<Vora.Application.Media.IMediaDedupeManager>();
        var progress = sp.GetRequiredService<ITaskProgressReporter>();
        var logger = sp.GetService<ILogger<TaskQueueManager>>();

        // No single show/movie may hold the whole library hostage. If a unit's
        // enrich stalls (an unresponsive provider, a title that triggers a slow
        // path), it's abandoned after this budget so the scan finishes and the
        // deferred passes (overlays, analysis) still run. The abandoned task is
        // left to finish in the background; its exceptions are observed/logged.
        var unitTimeout = TimeSpan.FromMinutes(5);

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
            ? await libraryManager.DiscoverScanUnitsAsync(libraryId, ct)
            : new List<ScanUnit>();

        var workflowStopwatch = Stopwatch.StartNew();

        if (units.Count > 0 && libraryType.HasValue)
        {
            var total = units.Count;
            var done = 0;
            var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 6);
            var scanStopwatch = Stopwatch.StartNew();
            var unitTimings = new ConcurrentBag<(string Label, double Seconds)>();
            await Parallel.ForEachAsync(
                units,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
                async (unit, unitCt) =>
                {
                    progress.Report($"Scanning & loading {CleanUnitLabel(unit.Label)}…");
                    var unitStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var unitTask = libraryManager.ScanAndEnrichUnitAsync(libraryId, libraryType.Value, unit.FilePaths, forceOverride, unitCt);
                        var finished = await Task.WhenAny(unitTask, Task.Delay(unitTimeout, unitCt));
                        if (finished == unitTask)
                        {
                            await unitTask;
                        }
                        else
                        {
                            logger?.LogWarning("Scan unit '{Unit}' exceeded {Timeout} and was abandoned; continuing the library scan.", CleanUnitLabel(unit.Label), unitTimeout);
                            _ = unitTask.ContinueWith(t => logger?.LogError(t.Exception, "Abandoned scan unit '{Unit}' later faulted.", CleanUnitLabel(unit.Label)),
                                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* one failing show/movie shouldn't abort the whole library */ }

                    unitStopwatch.Stop();
                    unitTimings.Add((CleanUnitLabel(unit.Label), unitStopwatch.Elapsed.TotalSeconds));

                    var n = Interlocked.Increment(ref done);
                    progress.Report($"Scanning & loading {CleanUnitLabel(unit.Label)}… ({n}/{total})");
                });

            scanStopwatch.Stop();
            var avg = unitTimings.IsEmpty ? 0 : unitTimings.Average(t => t.Seconds);
            var slowest = unitTimings.OrderByDescending(t => t.Seconds).Take(5)
                .Select(t => $"{t.Label} {t.Seconds:n1}s");
            logger?.LogInformation(
                "Scan+enrich for library {LibraryId}: {Units} units in {Wall:n1}s wall ({Parallelism}-way, avg {Avg:n1}s/unit). Slowest: {Slowest}",
                libraryId, total, scanStopwatch.Elapsed.TotalSeconds, parallelism, avg, string.Join(", ", slowest));
        }
        else
        {
            // Music (or nothing discovered): whole-library scan then enrich.
            var musicStopwatch = Stopwatch.StartNew();
            await libraryManager.TriggerLibraryFolderAndFileScanAsync(libraryId, ct);
            await metadataManager.TriggerLibraryEnrichmentAsync(libraryId, forceOverride: false, cancellationToken: ct);
            logger?.LogInformation("Scan+enrich (whole-library) for {LibraryId} took {Wall:n1}s.", libraryId, musicStopwatch.Elapsed.TotalSeconds);
        }

        // The deferred passes are independent: overlays, actor metadata,
        // analysis, and marker detection each stand alone. Run each in its own
        // try/catch so one failing pass can't starve the ones after it — a
        // library must never end up with, say, no analysis just because overlay
        // generation threw. Cancellation still stops the whole workflow.
        async Task RunStepAsync(string label, Func<Task> step)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(label);
            var stepStopwatch = Stopwatch.StartNew();
            try
            {
                await step();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Library workflow step '{Step}' failed for {LibraryId}; continuing with the remaining steps.", label, libraryId);
            }
            finally
            {
                stepStopwatch.Stop();
                logger?.LogInformation("Library workflow phase '{Step}' took {Wall:n1}s for {LibraryId}.", label.TrimEnd('…', '.', ' '), stepStopwatch.Elapsed.TotalSeconds, libraryId);
            }
        }

        // Safety net for anything the per-unit path missed (never double-fetches
        // an already-enriched item; force stays off here so a force rescan the
        // units already handled isn't re-run).
        await RunStepAsync("Fetching details…", () => metadataManager.TriggerLibraryEnrichmentAsync(libraryId, forceOverride: false, cancellationToken: ct));

        // A show scanned across two resolution folders (e.g. .../TV/1080p/Show
        // and .../TV/4K/Show) lands as two show rows until enrichment stamps the
        // shared external id. Now that ids are set, fold the duplicates together
        // so a 4K episode becomes a second part on its 1080p episode instead of a
        // parallel show — the same multi-version result movies get. No-op when
        // there are no duplicates.
        if (libraryType == LibraryType.TvShow)
        {
            await RunStepAsync("Merging duplicate shows…", () => dedupeManager.MergeDuplicateTvShowsAsync(libraryId, ct));
        }

        await RunStepAsync("Refreshing actor metadata…", () => metadataManager.TriggerActorMetadataRefreshAsync(ct));

        // Analysis populates each part's audio/video tracks (codec, HDR). The
        // overlay badges read that data, so analysis MUST run before overlays —
        // otherwise the audio-codec / HDR badges have nothing to draw and the
        // poster is overlaid with only the scan-time data (resolution). Marker
        // detection runs here too so the stinger badge is available.
        await RunStepAsync("Analyzing media…", () => analyzerManager.TriggerLibraryFileAnalysisAsync(libraryId, libraryName, ct));

        // Scanning a library is an addition event, so marker detection here runs
        // as an addition trigger — it is gated by RunDetections and stays off
        // entirely when detection is set to Never or Schedule-only. Only the
        // explicit Analyze action (forceOverride) detects regardless of setting.
        await RunStepAsync("Detecting intro/credit markers…", () => analyzerManager.TriggerLibrarySilenceDetectionAsync(libraryId, forceOverride: forceOverride, isAdditionTrigger: true, cancellationToken: ct));

        // Overlays LAST: now the posters get every badge — resolution (scan),
        // content rating (enrich), audio/video codec + HDR (analysis), and
        // stinger (markers). Running this before analysis is what left movies
        // with no audio-codec badge. Skipped entirely when no template is
        // configured and nothing was ever overlaid — otherwise the step (and its
        // progress label) would show on every scan even for users who never
        // touched poster overlays. When a template was deleted, previously
        // overlaid items still resolve as pending so their posters revert.
        if (await overlayManager.HasPendingOverlayWorkAsync(libraryId, ct))
        {
            await RunStepAsync("Generating poster overlays…", () => overlayManager.RunLibraryOverlaySyncAsync(libraryId, ct));
        }

        workflowStopwatch.Stop();
        logger?.LogInformation("Full library workflow for {LibraryId} completed in {Wall:n1}s.", libraryId, workflowStopwatch.Elapsed.TotalSeconds);
        progress.Report(null);
    }

    private static string ResolveDisplayName(Guid id, string? name) =>
        string.IsNullOrEmpty(name) ? id.ToString() : name;

    // All heavy jobs on one library share this key so they serialize (a scan,
    // refresh, analyze, or watcher file-ingest of the same library never overlap
    // and race on its rows); different libraries get different keys and can run
    // concurrently up to the global cap.
    private static string LibraryKey(Guid libraryId) => $"library:{libraryId}";

    // The long maintenance jobs (Analyze, Thumbnails) get a separate key from the
    // LibraryKey ingestion work, so a multi-day analyze no longer blocks new-file
    // scans and metadata refreshes for the same library — they run in another
    // concurrency slot. Analyze and Thumbnails still share THIS key so the two
    // heavy FFmpeg/GPU jobs on one library don't run at once and double the load.
    private static string LibraryMaintenanceKey(Guid libraryId) => $"library-maint:{libraryId}";

    // All of a collection's sync/order tasks share one key so they serialize:
    // a content sync (which itself queues a reorder), the chronology sort, and a
    // reevaluate can never run at the same time on one collection and race each
    // other's writes or double-spend the AI. The deferred one runs after and
    // no-ops via the chronology signature. Different collections stay parallel.
    private static string CollectionKey(Guid collectionId) => $"collection:{collectionId}";

    // Every poster-overlay sync and the orphan sweep share ONE key so they
    // serialize server-wide. The frontend "update now" fires a global (all
    // libraries) sync; without this, clicking it repeatedly — or a nightly
    // per-library sync overlapping a global one — runs several syncs at once,
    // and they race writing the same MediaItem rows in parallel DbContexts.
    // The sweep shares the key too so it never deletes a file mid-generation.
    private const string OverlaySyncKey = "poster-overlay-sync";

    private static string CleanUnitLabel(string label)
    {
        // Strip the trailing external-id tag (e.g. " [imdb-tt0115082]") so the
        // task shows "3rd Rock from the Sun (1996)" instead of the raw folder.
        var idx = label.IndexOf(" [", StringComparison.Ordinal);
        return idx > 0 ? label[..idx].Trim() : label.Trim();
    }
}
