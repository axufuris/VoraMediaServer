using System.Linq.Expressions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Tests.Collections;

public class CollectionManagerSeasonTests
{
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly IServerCalendar _calendar = Substitute.For<IServerCalendar>();
    private readonly Guid _library = Guid.NewGuid();

    private CollectionManager Manager() =>
        new(_repo, Substitute.For<ITaskQueueManager>(), Substitute.For<IClientNotifier>(), _calendar);

    private static CollectionSummaryVM Halloween() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Halloween",
        ItemCount = 12,
        VisibleStartDate = new DateTime(2000, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        VisibleEndDate = new DateTime(2000, 10, 31, 0, 0, 0, DateTimeKind.Utc),
        VisibleEveryYear = true,
    };

    private static CollectionSummaryVM Marvel() => new() { Id = Guid.NewGuid(), Title = "Marvel", ItemCount = 30 };

    private void Today(int year, int month, int day) => _calendar.TodayAsync().Returns(new DateOnly(year, month, day));

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
    public async Task A_repeating_collection_shows_in_its_season_every_year()
    {
        LibraryHolds(Halloween(), Marvel());
        Today(2031, 10, 31);

        var shown = await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>());

        shown.Select(c => c.Title).Should().BeEquivalentTo("Halloween", "Marvel");
    }

    [Fact]
    public async Task Viewers_do_not_see_a_collection_out_of_its_season()
    {
        LibraryHolds(Halloween(), Marvel());
        Today(2026, 11, 1);

        var shown = await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>());

        shown.Select(c => c.Title).Should().Equal("Marvel");
    }

    [Fact]
    public async Task An_admin_showing_hidden_collections_sees_it_marked_out_of_season()
    {
        LibraryHolds(Halloween(), Marvel());
        Today(2026, 11, 1);

        var shown = (await Manager().GetLibraryCollectionsAsync(_library, true, new List<Guid>(), includeHidden: true)).ToList();

        shown.Single(c => c.Title == "Halloween").HiddenReason.Should().Be(CollectionHiddenReason.OutOfSeason);
        shown.Single(c => c.Title == "Marvel").HiddenReason.Should().BeNull();
    }

    [Fact]
    public async Task A_global_collection_follows_its_season_too()
    {
        GlobalHolds(Halloween(), Marvel());
        Today(2026, 3, 1);

        (await Manager().GetGlobalCollectionsAsync(true, new List<Guid>())).Select(c => c.Title).Should().Equal("Marvel");

        var forAdmin = (await Manager().GetGlobalCollectionsAsync(true, new List<Guid>(), includeHidden: true)).ToList();
        forAdmin.Single(c => c.Title == "Halloween").HiddenReason.Should().Be(CollectionHiddenReason.OutOfSeason);
    }
}
