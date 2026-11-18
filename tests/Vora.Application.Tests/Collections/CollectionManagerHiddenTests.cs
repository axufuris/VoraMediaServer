using System.Linq.Expressions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Requests;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Tests.Collections;

// An admin can hide any collection, a system one included, and a global
// collection listed in a library shows every item it holds.
public class CollectionManagerHiddenTests
{
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly IServerCalendar _calendar = Substitute.For<IServerCalendar>();
    private readonly Guid _library = Guid.NewGuid();

    public CollectionManagerHiddenTests() => _calendar.TodayAsync().Returns(new DateOnly(2026, 11, 14));

    private CollectionManager Manager() =>
        new(_repo, Substitute.For<ITaskQueueManager>(), Substitute.For<IClientNotifier>(), _calendar);

    private static CollectionSummaryVM Summary(string title, int items, int inLibrary, bool hidden = false, bool systemGenerated = false) =>
        new() { Id = Guid.NewGuid(), Title = title, ItemCount = items, LibraryItemCount = inLibrary, Hidden = hidden, SystemGenerated = systemGenerated };

    private void LibraryHolds(params CollectionSummaryVM[] collections)
    {
        _repo.GetLibraryMinimumCollectionSizeAsync(_library).Returns(3);
        _repo.GetAllProjectedAsync(Arg.Any<Expression<Func<Collection, CollectionSummaryVM>>>(), Arg.Is<Guid?>(_library), Arg.Is(false), Arg.Any<bool>(), Arg.Any<List<Guid>?>())
            .Returns(collections);
    }

    private void GlobalHolds(params CollectionSummaryVM[] collections) =>
        _repo.GetAllProjectedAsync(Arg.Any<Expression<Func<Collection, CollectionSummaryVM>>>(), Arg.Is<Guid?>(id => id == null), Arg.Is(true), Arg.Any<bool>(), Arg.Any<List<Guid>?>())
            .Returns(collections);

    [Fact]
    public async Task Viewers_do_not_see_a_hidden_system_collection_and_an_admin_sees_it_marked_hidden()
    {
        LibraryHolds(Summary("John Wick Collection", 4, 4, hidden: true, systemGenerated: true), Summary("Marvel", 30, 30));

        (await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>())).Select(c => c.Title).Should().Equal("Marvel");

        var forAdmin = (await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>(), includeHidden: true)).ToList();
        forAdmin.Single(c => c.Title == "John Wick Collection").HiddenReason.Should().Be(CollectionHiddenReason.Hidden);
        forAdmin.Single(c => c.Title == "Marvel").HiddenReason.Should().BeNull();
    }

    [Fact]
    public async Task A_hidden_global_collection_is_kept_from_viewers_and_marked_for_an_admin()
    {
        GlobalHolds(Summary("John Wick", 5, 0, hidden: true), Summary("Marvel", 30, 0));

        (await Manager().GetGlobalCollectionsAsync(true, new List<Guid>())).Select(c => c.Title).Should().Equal("Marvel");

        var forAdmin = (await Manager().GetGlobalCollectionsAsync(true, new List<Guid>(), includeHidden: true)).ToList();
        forAdmin.Single(c => c.Title == "John Wick").HiddenReason.Should().Be(CollectionHiddenReason.Hidden);
    }

    [Fact]
    public async Task A_global_collection_in_a_library_counts_every_item_it_holds()
    {
        // Four films in this library and The Continental in the shows library.
        LibraryHolds(Summary("John Wick", 5, 4));

        var shown = (await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>())).Single();

        shown.ItemCount.Should().Be(5);
        shown.HiddenReason.Should().BeNull();
    }

    [Fact]
    public async Task A_global_collection_with_nothing_in_a_library_stays_out_of_it()
    {
        LibraryHolds(Summary("Star Wars Universe", 31, 0), Summary("Marvel", 30, 30));

        (await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>())).Select(c => c.Title).Should().Equal("Marvel");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hiding_or_showing_a_collection_saves_only_that_and_tells_open_pages(bool hidden)
    {
        var collection = new Collection { Title = "John Wick Collection", Hidden = !hidden };
        var notifier = Substitute.For<IClientNotifier>();
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);
        var manager = new CollectionManager(_repo, Substitute.For<ITaskQueueManager>(), notifier, _calendar);

        var found = await manager.SetHiddenAsync(collection.Id, hidden);

        found.Should().BeTrue();
        collection.Hidden.Should().Be(hidden);
        await _repo.Received(1).UpdateCollectionAsync(collection);
        await notifier.Received(1).NotifyCollectionUpdatedAsync(collection.Id);
    }

    [Fact]
    public async Task Hiding_a_collection_that_is_gone_saves_nothing()
    {
        var id = Guid.NewGuid();
        _repo.GetForUpdateAsync(id).Returns((Collection?)null);

        (await Manager().SetHiddenAsync(id, true)).Should().BeFalse();

        await _repo.DidNotReceive().UpdateCollectionAsync(Arg.Any<Collection>());
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public async Task Saving_sets_hidden_only_when_the_request_says(bool? requested, bool before, bool after)
    {
        var collection = new Collection { Title = "John Wick Collection", SystemGenerated = true, Hidden = before };
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);

        await Manager().UpdateCollectionAsync(collection.Id, new UpdateCollectionRequest { Title = collection.Title, Hidden = requested });

        collection.Hidden.Should().Be(after);
        await _repo.Received(1).UpdateCollectionAsync(collection);
    }
}
