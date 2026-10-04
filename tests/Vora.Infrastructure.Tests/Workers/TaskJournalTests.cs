using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Tasks;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public class TaskJournalTests
{
    private static readonly Guid Library = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _database = "task-journal-" + Guid.NewGuid().ToString("N");
    private readonly ITaskQueueManager _queue = Substitute.For<ITaskQueueManager>();
    private readonly ServiceProvider _services;

    public TaskJournalTests()
    {
        var collection = new ServiceCollection();
        collection.AddDbContext<VoraDbContext>(o => o.UseInMemoryDatabase(_database));
        collection.AddSingleton(_queue);
        _services = collection.BuildServiceProvider();
    }

    private TaskJournal NewJournal() =>
        new(_services, _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TaskJournal>.Instance);

    private async Task<List<PendingTask>> SavedAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<VoraDbContext>().PendingTasks.AsNoTracking().OrderBy(t => t.Sequence).ToListAsync(TestContext.Current.CancellationToken);
    }

    private static TaskRecipe ScanLibrary(Guid libraryId) =>
        TaskRecipe.Of(nameof(ITaskQueueManager.QueueScanLibrary), new { libraryId, libraryName = "Movies", forceOverride = false });

    [Fact]
    public async Task Queued_tasks_are_saved_and_finished_ones_removed()
    {
        var journal = NewJournal();
        var kept = Guid.NewGuid();
        var finished = Guid.NewGuid();
        journal.Record(kept, "Scan Library: Movies", ScanLibrary(Library));
        journal.Record(finished, "Scan Library: Shows", ScanLibrary(Guid.NewGuid()));
        await journal.FlushAsync(TestContext.Current.CancellationToken);

        journal.Complete(finished);
        await journal.FlushAsync(TestContext.Current.CancellationToken);

        (await SavedAsync()).Select(t => t.Id).Should().Equal(kept);
    }

    [Fact]
    public async Task Saving_a_task_again_keeps_its_place_in_line()
    {
        var journal = NewJournal();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        journal.Record(first, "Analyze Library: Shows", TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryAnalysis), new { libraryId = Library, reason = 1 }));
        journal.Record(second, "Scan Library: Movies", ScanLibrary(Library));
        await journal.FlushAsync(TestContext.Current.CancellationToken);

        journal.Record(first, "Analyze Library: Shows", TaskRecipe.Of(nameof(ITaskQueueManager.QueueLibraryAnalysis), new { libraryId = Library, reason = 3 }));
        await journal.FlushAsync(TestContext.Current.CancellationToken);

        var saved = await SavedAsync();
        saved.Select(t => t.Id).Should().Equal(first, second);
        TaskRecipe.FromJson(saved[0].Kind, saved[0].ArgumentsJson).Number("reason").Should().Be(3);
    }

    [Fact]
    public async Task A_task_finished_before_it_was_ever_saved_leaves_nothing_behind()
    {
        var journal = NewJournal();
        var id = Guid.NewGuid();
        journal.Record(id, "Scan Library: Movies", ScanLibrary(Library));
        journal.Complete(id);

        await journal.FlushAsync(TestContext.Current.CancellationToken);

        (await SavedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task On_startup_saved_tasks_are_queued_again_in_their_original_order()
    {
        var earlier = NewJournal();
        var shows = Guid.NewGuid();
        earlier.Record(Guid.NewGuid(), "Scan Library: Movies", ScanLibrary(Library));
        earlier.Record(Guid.NewGuid(), "Pre-extract Subtitles: Shows", TaskRecipe.Of(nameof(ITaskQueueManager.QueuePreExtractLibrarySubtitles), new { libraryId = shows, libraryName = "Shows" }));
        earlier.Record(Guid.NewGuid(), "Something old", TaskRecipe.Of("QueueSomethingRemoved"));
        await earlier.FlushAsync(TestContext.Current.CancellationToken);

        await NewJournal().RestoreAsync(TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _queue.QueueScanLibrary(Library, "Movies", false);
            _queue.QueuePreExtractLibrarySubtitles(shows, "Shows");
        });
        (await SavedAsync()).Should().BeEmpty();
    }
}
