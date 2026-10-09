using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Posters;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Tasks;

public class ResumableTaskTests
{
    private static readonly Guid Library = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Clicked = new(2026, 10, 8, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Queued = new(2026, 10, 8, 14, 0, 5, DateTimeKind.Utc);

    private sealed class RecordingJournal : ITaskJournal
    {
        public List<TaskRecipe> Recipes { get; } = new();
        public void Record(Guid taskId, string name, TaskRecipe recipe) => Recipes.Add(recipe);
        public void Complete(Guid taskId) { }
    }

    private readonly RecordingJournal _journal = new();
    private readonly TaskQueueManager _queue;
    private readonly IMediaAnalyzerManager _analyzer = Substitute.For<IMediaAnalyzerManager>();
    private readonly IVideoThumbnailManager _thumbnails = Substitute.For<IVideoThumbnailManager>();
    private readonly IMetadataManager _metadata = Substitute.For<IMetadataManager>();

    public ResumableTaskTests()
    {
        _queue = new TaskQueueManager(Substitute.For<IClientNotifier>(), _journal);
    }

    private async Task RunNextAsync()
    {
        var sp = new ServiceCollection()
            .AddSingleton(_analyzer).AddSingleton(_thumbnails).AddSingleton(_metadata)
            .AddSingleton(Substitute.For<IPosterOverlayManager>())
            .AddSingleton(Substitute.For<IMusicManager>())
            .AddSingleton<ITaskProgressReporter>(new NullTaskProgressReporter())
            .BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            await task.WorkItem(CancellationToken.None, sp);
            return;
        }
    }

    private DateTime? SavedSince() => _journal.Recipes[^1].Time("since");

    [Fact]
    public void Re_analyze_all_saves_when_it_was_asked_for()
    {
        var before = DateTime.UtcNow;

        _queue.QueueAnalyzeLibraryMediaContent(Library, "Movies", forceOverride: true);

        SavedSince().Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void An_analysis_that_is_not_forced_saves_no_time()
    {
        _queue.QueueAnalyzeLibraryMediaContent(Library, "Movies");

        SavedSince().Should().BeNull();
    }

    [Fact]
    public void Asking_again_before_the_run_starts_moves_the_time_forward()
    {
        _queue.QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Clicked);
        _queue.QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Clicked.AddHours(1));
        _queue.QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Clicked.AddMinutes(30));

        SavedSince().Should().Be(Clicked.AddHours(1));
        _queue.GetAllTasks().Should().ContainSingle();
    }

    [Fact]
    public async Task Re_analyze_all_only_redoes_what_was_analyzed_before_it_was_asked_for()
    {
        _queue.QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Clicked);

        await RunNextAsync();

        await _analyzer.Received(1).TriggerLibrarySilenceDetectionAsync(Library, "Movies", true, false, false, Clicked, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regenerating_all_thumbnails_only_redoes_what_was_made_before_it_was_asked_for()
    {
        _queue.QueueLibraryThumbnails(Library, "Movies", LibraryThumbnailReason.Force, Clicked);

        await RunNextAsync();

        await _thumbnails.Received(1).TriggerLibraryThumbnailGenerationAsync(Library, true, false, false, Clicked, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_forced_metadata_refresh_works_item_by_item_from_when_it_was_asked_for()
    {
        _queue.QueueRefreshLibraryMetadata(Library, "Movies", forceOverride: true, Clicked);

        await RunNextAsync();

        await _metadata.Received(1).TriggerLibraryEnrichmentAsync(Library, true, Clicked, Arg.Any<CancellationToken>());
        await _metadata.DidNotReceiveWithAnyArgs().TriggerLibraryMetadataRefreshAsync(default, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_metadata_refresh_that_is_not_forced_fills_in_what_is_missing()
    {
        _queue.QueueRefreshLibraryMetadata(Library, "Movies");

        await RunNextAsync();

        await _metadata.Received(1).TriggerLibraryMetadataRefreshAsync(Library, null, false, Arg.Any<CancellationToken>());
        await _metadata.DidNotReceiveWithAnyArgs().TriggerLibraryEnrichmentAsync(default, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_restored_forced_run_picks_up_from_its_saved_time()
    {
        var restored = Substitute.For<ITaskQueueManager>();
        var recipe = TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryAnalysis), new { libraryId = Library, libraryName = "Movies", reason = (int)LibraryAnalysisReason.Force })
            .WithTime("since", Clicked);

        TaskRecipes.Restore(restored, TaskRecipe.FromJson(recipe.Kind, recipe.ArgumentsJson), Queued).Should().BeTrue();

        restored.Received(1).QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Clicked);
    }

    [Fact]
    public void A_forced_run_saved_by_1_0_picks_up_from_when_it_was_queued()
    {
        var restored = Substitute.For<ITaskQueueManager>();

        TaskRecipes.Restore(restored, TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryAnalysis), new { libraryId = Library, libraryName = "Movies", reason = (int)LibraryAnalysisReason.Force }), Queued);
        TaskRecipes.Restore(restored, TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryThumbnails), new { libraryId = Library, libraryName = "Movies", reason = (int)LibraryThumbnailReason.Force }), Queued);
        TaskRecipes.Restore(restored, TaskRecipe.Of(nameof(ITaskQueueManager.QueueRefreshLibraryMetadata), new { libraryId = Library, libraryName = "Movies", forceOverride = true }), Queued);

        restored.Received(1).QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Force, Queued);
        restored.Received(1).QueueLibraryThumbnails(Library, "Movies", LibraryThumbnailReason.Force, Queued);
        restored.Received(1).QueueRefreshLibraryMetadata(Library, "Movies", true, Queued);
    }

    [Fact]
    public void A_run_that_was_not_forced_restores_without_a_time()
    {
        var restored = Substitute.For<ITaskQueueManager>();

        TaskRecipes.Restore(restored, TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryAnalysis), new { libraryId = Library, libraryName = "Movies", reason = (int)LibraryAnalysisReason.Manual }), Queued);

        restored.Received(1).QueueLibraryAnalysis(Library, "Movies", LibraryAnalysisReason.Manual, null);
    }

    [Fact]
    public void A_saved_time_reads_back_as_the_same_moment()
    {
        var recipe = TaskRecipe.Of("Anything").WithTime("since", Clicked);

        var read = TaskRecipe.FromJson(recipe.Kind, recipe.ArgumentsJson).Time("since");

        read.Should().Be(Clicked);
        read.GetValueOrDefault().Kind.Should().Be(DateTimeKind.Utc);
    }
}
