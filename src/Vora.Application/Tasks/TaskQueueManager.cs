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

namespace Vora.Application.Tasks;

public interface ITaskQueueManager
{
    void QueueLibraryAdded(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueLibraryUpdated(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueScanLibrary(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueRefreshLibraryMetadata(Guid libraryId, string? libraryName = null, bool forceOverride = false);
    void QueueAnalyzeLibraryMediaContent(Guid libraryId, string? libraryName = null, bool forceOverride = false, bool isScheduleTrigger = false);
    void QueueScanMediaItem(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueRefreshMediaItemMetadata(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueAnalyzeMediaItemContent(Guid mediaItemId, string? mediaItemName = null, bool forceOverride = false);
    void QueueArtworkProviderSwap(Guid libraryId, string libraryName);
    void QueueRefreshLibraryRatings(Guid libraryId, bool forceOverride = false);
    void QueueRefreshLibraryArtwork(Guid libraryId, bool forceOverride = false);
    void QueueRefreshMediaItemArtwork(Guid mediaItemId, bool forceOverride = false);
    void QueueRefreshArtistArtwork(Guid artistId, string? artistName = null, bool forceOverride = false);
    void QueueRefreshAlbumArtwork(Guid albumId, string? albumName = null, bool forceOverride = false);
    void QueueRefreshAllActorMetadata();
    void QueueRemoveOrphanedMedia(string filePath);
    void QueueCollectionChronologySync(Guid collectionId, string title);
    void QueueCollectionContentSync(Guid collectionId, string title);
    void QueueGeneratePosterOverlays(Guid mediaItemId);
    void QueueFullCollectionSync(Guid collectionId, string title, bool hasContentSync, bool hasChronologySort);
    void QueueReevaluateCollectionOrder(Guid collectionId, Guid mediaItemId);
    Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem);
    bool CancelTask(Guid taskId);
    IAsyncEnumerable<QueuedTaskDto> DequeueAsync(CancellationToken cancellationToken);
    void MarkTaskAsRunning(Guid taskId);
    void RemoveTask(Guid taskId);
    IEnumerable<QueuedTaskVM> GetAllTasks();
    void QueueGenerateAiEmbeddings();
    void QueueGenerateLibraryPosterOverlays(Guid libraryId);
    void QueueIptvEpgSync();
}

public class TaskQueueManager : ITaskQueueManager
{
    private const int AiEmbeddingsBatchSize = 100;
    private const string RunningStatus = "Running";

    private readonly IClientNotifier _notifier;
    private readonly Channel<QueuedTaskDto> _queue = Channel.CreateUnbounded<QueuedTaskDto>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _taskTokens = new();
    private readonly ConcurrentDictionary<Guid, QueuedTaskDto> _taskStates = new();

    public TaskQueueManager(IClientNotifier notifier)
    {
        _notifier = notifier;
    }

    public void QueueLibraryAdded(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Auto-Ingest Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: true));
    }

    public void QueueLibraryUpdated(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Update Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: false));
    }

    public void QueueScanLibrary(Guid libraryId, string? libraryName = null, bool forceOverride = false)
    {
        EnqueueTask($"Scan Library: {ResolveDisplayName(libraryId, libraryName)}", (ct, sp) =>
            RunFullLibraryWorkflowAsync(sp, libraryId, libraryName, forceOverride, isAdditionTrigger: false));
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
            await analyzerManager.TriggerMediaItemSilenceDetectionAsync(mediaItemId, mediaItemName, forceOverride: forceOverride, isAdditionTrigger: true);
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
        });
    }

    public void QueueRefreshLibraryArtwork(Guid libraryId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Artwork for Library: {libraryId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride);

            await overlayManager.RunLibraryOverlaySyncAsync(libraryId);
        });
    }

    public void QueueRefreshMediaItemArtwork(Guid mediaItemId, bool forceOverride = false)
    {
        EnqueueTask($"Refresh Artwork for Media Item: {mediaItemId}", async (ct, sp) =>
        {
            var metadataManager = sp.GetRequiredService<IMetadataManager>();
            var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

            await metadataManager.TriggerMediaItemArtworkRefreshAsync(mediaItemId, forceOverride);

            await overlayManager.GenerateOverlaysForMediaAsync(mediaItemId);
        });
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

    public void QueueRemoveOrphanedMedia(string filePath)
    {
        EnqueueTask($"Auto-Cleanup: {Path.GetFileName(filePath)}", async (ct, sp) =>
        {
            var mediaRepo = sp.GetRequiredService<IMediaRepository>();
            await mediaRepo.DeleteMediaByFilePathAsync(filePath);
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
        });
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

    public void QueueReevaluateCollectionOrder(Guid collectionId, Guid mediaItemId)
    {
        EnqueueTask("Reevaluate Collection Order", async (ct, sp) =>
        {
            var orderingService = sp.GetRequiredService<CollectionOrderingService>();
            await orderingService.ReevaluateOrderOnItemAddedAsync(collectionId, mediaItemId);
        });
    }

    public Guid EnqueueTask(string name, Func<CancellationToken, IServiceProvider, Task> workItem)
    {
        var task = new QueuedTaskDto { Name = name, WorkItem = workItem };
        var cts = new CancellationTokenSource();

        _taskTokens.TryAdd(task.Id, cts);
        _taskStates.TryAdd(task.Id, task);

        _queue.Writer.TryWrite(task);

        _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());

        return task.Id;
    }

    public void MarkTaskAsRunning(Guid taskId)
    {
        if (_taskStates.TryGetValue(taskId, out var state))
        {
            state.Status = RunningStatus;
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
        }
    }

    public bool CancelTask(Guid taskId)
    {
        if (_taskTokens.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
            _taskTokens.TryRemove(taskId, out _);
            _ = Task.Run(() => _notifier.NotifyTasksUpdatedAsync());
            return true;
        }
        return false;
    }

    public IAsyncEnumerable<QueuedTaskDto> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }

    public void RemoveTask(Guid taskId)
    {
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
                Status = t.Status
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

    public void QueueGenerateLibraryPosterOverlays(Guid libraryId)
    {
        EnqueueTask($"Generate Library Poster Overlays: {libraryId}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IPosterOverlayManager>();
            await manager.RunLibraryOverlaySyncAsync(libraryId);
        });
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

    private static async Task RunFullLibraryWorkflowAsync(IServiceProvider sp, Guid libraryId, string? libraryName, bool forceOverride, bool isAdditionTrigger)
    {
        var metadataManager = sp.GetRequiredService<IMetadataManager>();
        var analyzerManager = sp.GetRequiredService<IMediaAnalyzerManager>();
        var libraryManager = sp.GetRequiredService<ILibraryManager>();
        var overlayManager = sp.GetRequiredService<IPosterOverlayManager>();

        await libraryManager.TriggerLibraryFolderAndFileScanAsync(libraryId);
        await analyzerManager.TriggerLibraryFileAnalysisAsync(libraryId, libraryName);

        await metadataManager.TriggerLibraryMetadataRefreshAsync(libraryId, forceOverride: forceOverride);
        await metadataManager.TriggerLibraryArtworkRefreshAsync(libraryId, forceOverride: forceOverride);
        await metadataManager.TriggerLibraryRatingsRefreshAsync(libraryId, forceOverride: forceOverride);
        await metadataManager.TriggerActorMetadataRefreshAsync();

        await overlayManager.RunLibraryOverlaySyncAsync(libraryId);

        await analyzerManager.TriggerLibrarySilenceDetectionAsync(libraryId, forceOverride: forceOverride, isAdditionTrigger: isAdditionTrigger);
    }

    private static string ResolveDisplayName(Guid id, string? name) =>
        string.IsNullOrEmpty(name) ? id.ToString() : name;
}
