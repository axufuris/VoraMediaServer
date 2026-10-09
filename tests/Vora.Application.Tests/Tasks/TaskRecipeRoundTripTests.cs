using FluentAssertions.Execution;
using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class TaskRecipeRoundTripTests
{
    private static readonly Guid Library = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Item = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Since = new(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc);

    private sealed class RecordingJournal : ITaskJournal
    {
        public List<(Guid Id, TaskRecipe Recipe)> Records { get; } = new();
        public List<Guid> Completed { get; } = new();
        public void Record(Guid taskId, string name, TaskRecipe recipe) => Records.Add((taskId, recipe));
        public void Complete(Guid taskId) => Completed.Add(taskId);
    }

    private sealed record Case(string Method, Action<ITaskQueueManager> Call, Action<ITaskQueueManager>? Restored = null);

    private static readonly Case[] Cases =
    [
        new(nameof(ITaskQueueManager.QueueLibraryAdded), q => q.QueueLibraryAdded(Library, "Movies", true, Since)),
        new(nameof(ITaskQueueManager.QueueLibraryUpdated), q => q.QueueLibraryUpdated(Library, null, false)),
        new(nameof(ITaskQueueManager.QueueScanLibrary), q => q.QueueScanLibrary(Library, "Movies", true, Since)),
        new(nameof(ITaskQueueManager.QueueDeleteLibrary), q => q.QueueDeleteLibrary(Library, "Movies")),
        new(nameof(ITaskQueueManager.QueueRefreshLibraryMetadata), q => q.QueueRefreshLibraryMetadata(Library, "Movies", true, Since)),
        new(nameof(ITaskQueueManager.QueueLibraryPostScan), q => q.QueueLibraryPostScan(Library, "Movies", true, Since),
            q => q.QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Addition | LibraryAnalysisReason.Force, Since)),
        new(nameof(ITaskQueueManager.QueueAnalyzeLibraryMediaContent), q => q.QueueAnalyzeLibraryMediaContent(Library, "Shows", isScheduleTrigger: true),
            q => q.QueueLibraryAnalysis(Library, "Shows", LibraryAnalysisReason.Schedule)),
        new(nameof(ITaskQueueManager.QueueLibraryAnalysis), q => q.QueueLibraryAnalysis(Library, "Shows", LibraryAnalysisReason.Manual)),
        new(nameof(ITaskQueueManager.QueueScanMediaItem), q => q.QueueScanMediaItem(Item, "Heat", true, Library)),
        new(nameof(ITaskQueueManager.QueueScanNewFile), q => q.QueueScanNewFile(Library, "/media/movies/Heat (1995)/Heat.mkv")),
        new(nameof(ITaskQueueManager.QueueScanNewMusicFile), q => q.QueueScanNewMusicFile(Library, "/media/music/song.flac")),
        new(nameof(ITaskQueueManager.QueueRefreshMediaItemMetadata), q => q.QueueRefreshMediaItemMetadata(Item, null, true, null)),
        new(nameof(ITaskQueueManager.QueueRefreshMatchedMediaItem), q => q.QueueRefreshMatchedMediaItem(Item, Library, true)),
        new(nameof(ITaskQueueManager.QueueAnalyzeMediaItemContent), q => q.QueueAnalyzeMediaItemContent(Item, "Heat", true)),
        new(nameof(ITaskQueueManager.QueueArtworkProviderSwap), q => q.QueueArtworkProviderSwap(Library, "Movies")),
        new(nameof(ITaskQueueManager.QueueRefreshLibraryRatings), q => q.QueueRefreshLibraryRatings(Library, true, Since)),
        new(nameof(ITaskQueueManager.QueueRefreshMediaItemArtwork), q => q.QueueRefreshMediaItemArtwork(Item, true, Library)),
        new(nameof(ITaskQueueManager.QueueRefreshArtistArtwork), q => q.QueueRefreshArtistArtwork(Item, "Muse", true)),
        new(nameof(ITaskQueueManager.QueueRefreshAlbumArtwork), q => q.QueueRefreshAlbumArtwork(Item, "Absolution", false)),
        new(nameof(ITaskQueueManager.QueueRefreshAllActorMetadata), q => q.QueueRefreshAllActorMetadata()),
        new(nameof(ITaskQueueManager.QueueResolveTvdbIds), q => q.QueueResolveTvdbIds()),
        new(nameof(ITaskQueueManager.QueueMergeDuplicateShows), q => q.QueueMergeDuplicateShows()),
        new(nameof(ITaskQueueManager.QueueRemoveOrphanedMedia), q => q.QueueRemoveOrphanedMedia("/media/movies/gone.mkv")),
        new(nameof(ITaskQueueManager.QueueCollectionChronologySync), q => q.QueueCollectionChronologySync(Other, "Marvel")),
        new(nameof(ITaskQueueManager.QueueCollectionContentSync), q => q.QueueCollectionContentSync(Other, "Marvel")),
        new(nameof(ITaskQueueManager.QueueGeneratePosterOverlays), q => q.QueueGeneratePosterOverlays(Item)),
        new(nameof(ITaskQueueManager.QueueFullCollectionSync), q => q.QueueFullCollectionSync(Other, "Marvel", true, false)),
        new(nameof(ITaskQueueManager.QueueReevaluateCollectionOrder), q => q.QueueReevaluateCollectionOrder(Other)),
        new(nameof(ITaskQueueManager.QueueGenerateAiEmbeddings), q => q.QueueGenerateAiEmbeddings()),
        new(nameof(ITaskQueueManager.QueueGenerateLibraryPosterOverlays), q => q.QueueGenerateLibraryPosterOverlays(Library, "Movies")),
        new(nameof(ITaskQueueManager.QueueOverlayOrphanSweep), q => q.QueueOverlayOrphanSweep()),
        new(nameof(ITaskQueueManager.QueueUnusedFileRemoval), q => q.QueueUnusedFileRemoval()),
        new(nameof(ITaskQueueManager.QueueIptvEpgSync), q => q.QueueIptvEpgSync()),
        new(nameof(ITaskQueueManager.QueueIptvHealthCheck), q => q.QueueIptvHealthCheck(Other, "Cable")),
        new(nameof(ITaskQueueManager.QueueGenerateLibraryVideoThumbnails), q => q.QueueGenerateLibraryVideoThumbnails(Library, "Movies", isAdditionTrigger: true),
            q => q.QueueLibraryThumbnails(Library, "Movies", LibraryThumbnailReason.Addition)),
        new(nameof(ITaskQueueManager.QueueLibraryThumbnails), q => q.QueueLibraryThumbnails(Library, "Movies", LibraryThumbnailReason.Force, Since)),
        new(nameof(ITaskQueueManager.QueueRemoveLibraryVideoThumbnails), q => q.QueueRemoveLibraryVideoThumbnails(Library, "Movies")),
        new(nameof(ITaskQueueManager.QueueGenerateMediaItemVideoThumbnails), q => q.QueueGenerateMediaItemVideoThumbnails(Item, "Heat", true)),
        new(nameof(ITaskQueueManager.QueuePreExtractMediaItemSubtitles), q => q.QueuePreExtractMediaItemSubtitles(Item, "Heat", Library)),
        new(nameof(ITaskQueueManager.QueuePreExtractLibrarySubtitles), q => q.QueuePreExtractLibrarySubtitles(Library, "Movies")),
        new(nameof(ITaskQueueManager.QueueSubtitleBackfill), q => q.QueueSubtitleBackfill()),
        new(nameof(ITaskQueueManager.QueueRefreshMusicPopularity), q => q.QueueRefreshMusicPopularity()),
        new(nameof(ITaskQueueManager.QueueRateMusicContent), q => q.QueueRateMusicContent()),
        new(nameof(ITaskQueueManager.QueueGenerateAiPlaylists), q => q.QueueGenerateAiPlaylists(true)),
        new(nameof(ITaskQueueManager.QueuePrepareMusicForAiPlaylists), q => q.QueuePrepareMusicForAiPlaylists()),
    ];

    [Fact]
    public void Every_queue_method_is_covered()
    {
        var queueMethods = typeof(ITaskQueueManager).GetMethods()
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Queue", StringComparison.Ordinal))
            .Distinct();

        Cases.Select(c => c.Method).Should().BeEquivalentTo(queueMethods);
    }

    [Fact]
    public void A_saved_task_restores_to_the_same_queue_call()
    {
        using var scope = new AssertionScope();
        foreach (var testCase in Cases)
        {
            var expected = Substitute.For<ITaskQueueManager>();
            (testCase.Restored ?? testCase.Call)(expected);
            var wanted = expected.ReceivedCalls().Single();

            var journal = new RecordingJournal();
            testCase.Call(new TaskQueueManager(Substitute.For<IClientNotifier>(), journal));
            journal.Records.Should().ContainSingle(testCase.Method);
            if (journal.Records.Count != 1) continue;

            var restored = Substitute.For<ITaskQueueManager>();
            TaskRecipes.Restore(restored, TaskRecipe.FromJson(journal.Records[0].Recipe.Kind, journal.Records[0].Recipe.ArgumentsJson))
                .Should().BeTrue(testCase.Method);
            var actual = restored.ReceivedCalls().Single();

            actual.GetMethodInfo().Name.Should().Be(wanted.GetMethodInfo().Name, testCase.Method);
            actual.GetArguments().Should().Equal(wanted.GetArguments(), testCase.Method);
        }
    }

    [Fact]
    public void An_unknown_kind_is_not_restored()
    {
        var queue = Substitute.For<ITaskQueueManager>();

        TaskRecipes.Restore(queue, TaskRecipe.FromJson("QueueSomethingRemoved", "{}")).Should().BeFalse();
        queue.ReceivedCalls().Should().BeEmpty();
    }
}
