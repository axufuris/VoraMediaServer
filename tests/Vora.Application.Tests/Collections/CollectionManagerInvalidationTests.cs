using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Requests;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Tests.Collections;

public class CollectionManagerInvalidationTests
{
    private readonly ICollectionRepository _repo;
    private readonly ITaskQueueManager _tasks;
    private readonly IClientNotifier _notifier;
    private readonly CollectionManager _manager;

    public CollectionManagerInvalidationTests()
    {
        _repo = Substitute.For<ICollectionRepository>();
        _tasks = Substitute.For<ITaskQueueManager>();
        _notifier = Substitute.For<IClientNotifier>();
        _manager = new CollectionManager(_repo, _tasks, _notifier);
    }

    private static Collection Existing(string? provider, string? externalId) => new()
    {
        Id = Guid.NewGuid(),
        Title = "MCU",
        SortProviderId = provider,
        ExternalListId = externalId
    };

    private static UpdateCollectionRequest Request(string? provider, string? externalId) => new()
    {
        Title = "MCU",
        SortProviderId = provider,
        ExternalListId = externalId
    };

    [Fact]
    public async Task UpdateCollectionAsync_resets_cached_years_when_ordering_description_changes()
    {
        var collection = Existing("openai_chronology", "MCU in release order");
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);

        await _manager.UpdateCollectionAsync(collection.Id, Request("openai_chronology", "MCU in chronological order"));

        await _repo.Received(1).ResetChronologyCacheAsync(collection.Id);
    }

    [Fact]
    public async Task UpdateCollectionAsync_resets_cached_years_when_sort_provider_changes()
    {
        var collection = Existing("trakt_list", "abc");
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);

        await _manager.UpdateCollectionAsync(collection.Id, Request("openai_chronology", "abc"));

        await _repo.Received(1).ResetChronologyCacheAsync(collection.Id);
    }

    [Fact]
    public async Task UpdateCollectionAsync_keeps_cached_years_when_ordering_is_unchanged()
    {
        var collection = Existing("openai_chronology", "MCU in chronological order");
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);

        await _manager.UpdateCollectionAsync(collection.Id, Request("openai_chronology", "MCU in chronological order"));

        await _repo.DidNotReceive().ResetChronologyCacheAsync(Arg.Any<Guid>());
    }
}
