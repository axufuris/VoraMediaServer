namespace Vora.Application.Tasks;

public static class TaskRecipes
{
    public static bool Restore(ITaskQueueManager queue, TaskRecipe r, DateTime? queuedAt = null)
    {
        DateTime? ForcedSince(bool forced) => forced ? r.Time("since") ?? queuedAt : null;

        switch (r.Kind)
        {
            case nameof(ITaskQueueManager.QueueLibraryAdded):
                queue.QueueLibraryAdded(r.Guid("libraryId"), r.Text("libraryName"), r.Flag("forceOverride"), ForcedSince(r.Flag("forceOverride")));
                return true;
            case nameof(ITaskQueueManager.QueueLibraryUpdated):
                queue.QueueLibraryUpdated(r.Guid("libraryId"), r.Text("libraryName"), r.Flag("forceOverride"), ForcedSince(r.Flag("forceOverride")));
                return true;
            case nameof(ITaskQueueManager.QueueScanLibrary):
                queue.QueueScanLibrary(r.Guid("libraryId"), r.Text("libraryName"), r.Flag("forceOverride"), ForcedSince(r.Flag("forceOverride")));
                return true;
            case nameof(ITaskQueueManager.QueueDeleteLibrary):
                queue.QueueDeleteLibrary(r.Guid("libraryId"), r.Text("libraryName"));
                return true;
            case nameof(ITaskQueueManager.QueueRefreshLibraryMetadata):
                queue.QueueRefreshLibraryMetadata(r.Guid("libraryId"), r.Text("libraryName"), r.Flag("forceOverride"), ForcedSince(r.Flag("forceOverride")));
                return true;
            case nameof(ITaskQueueManager.QueueLibraryAnalysis):
                var analysisReason = (LibraryAnalysisReason)r.Number("reason");
                queue.QueueLibraryAnalysis(r.Guid("libraryId"), r.Text("libraryName"), analysisReason, ForcedSince(analysisReason.HasFlag(LibraryAnalysisReason.Force)));
                return true;
            case nameof(ITaskQueueManager.QueueScanMediaItem):
                queue.QueueScanMediaItem(r.Guid("mediaItemId"), r.Text("mediaItemName"), r.Flag("forceOverride"), r.OptionalGuid("libraryId"));
                return true;
            case nameof(ITaskQueueManager.QueueScanNewFile):
                queue.QueueScanNewFile(r.Guid("libraryId"), r.Text("filePath") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueScanNewMusicFile):
                queue.QueueScanNewMusicFile(r.Guid("libraryId"), r.Text("filePath") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueRefreshMediaItemMetadata):
                queue.QueueRefreshMediaItemMetadata(r.Guid("mediaItemId"), r.Text("mediaItemName"), r.Flag("forceOverride"), r.OptionalGuid("libraryId"));
                return true;
            case nameof(ITaskQueueManager.QueueRefreshMatchedMediaItem):
                queue.QueueRefreshMatchedMediaItem(r.Guid("mediaItemId"), r.Guid("libraryId"), r.Flag("isTvShow"));
                return true;
            case nameof(ITaskQueueManager.QueueAnalyzeMediaItemContent):
                queue.QueueAnalyzeMediaItemContent(r.Guid("mediaItemId"), r.Text("mediaItemName"), r.Flag("forceOverride"));
                return true;
            case nameof(ITaskQueueManager.QueueArtworkProviderSwap):
                queue.QueueArtworkProviderSwap(r.Guid("libraryId"), r.Text("libraryName") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueRefreshLibraryRatings):
                queue.QueueRefreshLibraryRatings(r.Guid("libraryId"), r.Flag("forceOverride"), r.Time("since") ?? queuedAt);
                return true;
            case nameof(ITaskQueueManager.QueueRefreshMediaItemArtwork):
                queue.QueueRefreshMediaItemArtwork(r.Guid("mediaItemId"), r.Flag("forceOverride"), r.OptionalGuid("libraryId"));
                return true;
            case nameof(ITaskQueueManager.QueueRefreshArtistArtwork):
                queue.QueueRefreshArtistArtwork(r.Guid("artistId"), r.Text("artistName"), r.Flag("forceOverride"));
                return true;
            case nameof(ITaskQueueManager.QueueRefreshAlbumArtwork):
                queue.QueueRefreshAlbumArtwork(r.Guid("albumId"), r.Text("albumName"), r.Flag("forceOverride"));
                return true;
            case nameof(ITaskQueueManager.QueueRefreshAllActorMetadata):
                queue.QueueRefreshAllActorMetadata();
                return true;
            case nameof(ITaskQueueManager.QueueResolveTvdbIds):
                queue.QueueResolveTvdbIds();
                return true;
            case nameof(ITaskQueueManager.QueueMergeDuplicateShows):
                queue.QueueMergeDuplicateShows();
                return true;
            case nameof(ITaskQueueManager.QueueRemoveOrphanedMedia):
                queue.QueueRemoveOrphanedMedia(r.Text("filePath") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueCollectionChronologySync):
                queue.QueueCollectionChronologySync(r.Guid("collectionId"), r.Text("title") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueCollectionContentSync):
                queue.QueueCollectionContentSync(r.Guid("collectionId"), r.Text("title") ?? string.Empty);
                return true;
            case nameof(ITaskQueueManager.QueueGeneratePosterOverlays):
                queue.QueueGeneratePosterOverlays(r.Guid("mediaItemId"));
                return true;
            case nameof(ITaskQueueManager.QueueFullCollectionSync):
                queue.QueueFullCollectionSync(r.Guid("collectionId"), r.Text("title") ?? string.Empty, r.Flag("hasContentSync"), r.Flag("hasChronologySort"));
                return true;
            case nameof(ITaskQueueManager.QueueReevaluateCollectionOrder):
                queue.QueueReevaluateCollectionOrder(r.Guid("collectionId"));
                return true;
            case nameof(ITaskQueueManager.QueueGenerateAiEmbeddings):
                queue.QueueGenerateAiEmbeddings();
                return true;
            case nameof(ITaskQueueManager.QueueGenerateLibraryPosterOverlays):
                queue.QueueGenerateLibraryPosterOverlays(r.Guid("libraryId"), r.Text("libraryName"));
                return true;
            case nameof(ITaskQueueManager.QueueOverlayOrphanSweep):
                queue.QueueOverlayOrphanSweep();
                return true;
            case nameof(ITaskQueueManager.QueueUnusedFileRemoval):
                queue.QueueUnusedFileRemoval();
                return true;
            case nameof(ITaskQueueManager.QueueIptvEpgSync):
                queue.QueueIptvEpgSync();
                return true;
            case nameof(ITaskQueueManager.QueueIptvHealthCheck):
                queue.QueueIptvHealthCheck(r.Guid("playlistId"), r.Text("playlistName"));
                return true;
            case nameof(ITaskQueueManager.QueueLibraryThumbnails):
                var thumbnailReason = (LibraryThumbnailReason)r.Number("reason");
                queue.QueueLibraryThumbnails(r.Guid("libraryId"), r.Text("libraryName"), thumbnailReason, ForcedSince(thumbnailReason.HasFlag(LibraryThumbnailReason.Force)));
                return true;
            case nameof(ITaskQueueManager.QueueRemoveLibraryVideoThumbnails):
                queue.QueueRemoveLibraryVideoThumbnails(r.Guid("libraryId"), r.Text("libraryName"));
                return true;
            case nameof(ITaskQueueManager.QueueGenerateMediaItemVideoThumbnails):
                queue.QueueGenerateMediaItemVideoThumbnails(r.Guid("mediaItemId"), r.Text("mediaItemName"), r.Flag("forceOverride"));
                return true;
            case nameof(ITaskQueueManager.QueuePreExtractMediaItemSubtitles):
                queue.QueuePreExtractMediaItemSubtitles(r.Guid("mediaItemId"), r.Text("mediaItemName"));
                return true;
            case nameof(ITaskQueueManager.QueuePreExtractLibrarySubtitles):
                queue.QueuePreExtractLibrarySubtitles(r.Guid("libraryId"), r.Text("libraryName"));
                return true;
            case nameof(ITaskQueueManager.QueueSubtitleBackfill):
                queue.QueueSubtitleBackfill();
                return true;
            case nameof(ITaskQueueManager.QueueRefreshMusicPopularity):
                queue.QueueRefreshMusicPopularity();
                return true;
            case nameof(ITaskQueueManager.QueueRateMusicContent):
                queue.QueueRateMusicContent();
                return true;
            case nameof(ITaskQueueManager.QueueGenerateAiPlaylists):
                queue.QueueGenerateAiPlaylists(r.Flag("force"));
                return true;
            case nameof(ITaskQueueManager.QueuePrepareMusicForAiPlaylists):
                queue.QueuePrepareMusicForAiPlaylists();
                return true;
            default:
                return false;
        }
    }
}
