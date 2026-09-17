using System.Linq.Expressions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Collections;

public class CollectionManagerSortTests
{
    [Fact]
    public async Task Chronological_sort_puts_unordered_items_last_by_release_date()
    {
        var repo = Substitute.For<ICollectionRepository>();
        var manager = new CollectionManager(repo, Substitute.For<ITaskQueueManager>(), Substitute.For<IClientNotifier>());

        var collectionId = Guid.NewGuid();
        var ordered1 = new CollectionDetailsLibraryItemVM { Id = Guid.NewGuid(), Title = "First", ReleaseDate = new DateTime(2011, 1, 1) };
        var ordered2 = new CollectionDetailsLibraryItemVM { Id = Guid.NewGuid(), Title = "Second", ReleaseDate = new DateTime(2008, 1, 1) };
        var freshOld = new CollectionDetailsLibraryItemVM { Id = Guid.NewGuid(), Title = "FreshOld", ReleaseDate = new DateTime(2021, 1, 1) };
        var freshNew = new CollectionDetailsLibraryItemVM { Id = Guid.NewGuid(), Title = "FreshNew", ReleaseDate = new DateTime(2023, 1, 1) };

        var vm = new CollectionDetailsVM
        {
            Id = collectionId,
            DefaultSort = CollectionSortOrder.Chronological,
            Items = new List<CollectionDetailsLibraryItemVM> { freshNew, ordered2, freshOld, ordered1 }
        };

        repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionDetailsVM>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(vm);

        // ordered1 -> 1, ordered2 -> 2; freshOld stored as 0 (just added, not yet ordered); freshNew absent entirely
        repo.GetCollectionItemSortOrdersAsync(collectionId).Returns(new Dictionary<Guid, decimal>
        {
            [ordered1.Id] = 1m,
            [ordered2.Id] = 2m,
            [freshOld.Id] = 0m
        });

        var result = await manager.GetCollectionDetailsAsync(collectionId, null, true, new List<Guid>());

        Assert.NotNull(result);
        var titles = result!.Items.Select(i => i.Title).ToList();
        Assert.Equal(new[] { "First", "Second", "FreshOld", "FreshNew" }, titles);
    }
}
