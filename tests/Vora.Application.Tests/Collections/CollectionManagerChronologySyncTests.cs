using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Dtos;
using Vora.Application.Settings;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Collections;

public class CollectionManagerChronologySyncTests
{
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly ITaskQueueManager _queue = Substitute.For<ITaskQueueManager>();

    private CollectionManager Manager() =>
        new(_repo, _queue, Substitute.For<IClientNotifier>(), Substitute.For<IServerCalendar>());

    [Fact]
    public async Task Sync_timeline_queues_a_forced_task_and_returns_at_once()
    {
        var id = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(id).Returns(new CollectionChronologyConfigDto { Title = "Star Wars Universe", SortProviderId = "openai_chronology" });

        var refusal = await Manager().QueueChronologySyncAsync(id);

        refusal.Should().BeNull();
        _queue.Received(1).QueueCollectionChronologySync(id, "Star Wars Universe", true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Sync_timeline_on_a_collection_with_no_provider_queues_nothing(string? providerId)
    {
        var id = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(id).Returns(new CollectionChronologyConfigDto { Title = "Marvel", SortProviderId = providerId });

        (await Manager().QueueChronologySyncAsync(id)).Should().NotBeNullOrEmpty();

        _queue.DidNotReceiveWithAnyArgs().QueueCollectionChronologySync(default, default!, default);
    }

    [Fact]
    public async Task Sync_timeline_on_a_collection_that_is_gone_queues_nothing()
    {
        var id = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(id).Returns((CollectionChronologyConfigDto?)null);

        (await Manager().QueueChronologySyncAsync(id)).Should().NotBeNullOrEmpty();

        _queue.DidNotReceiveWithAnyArgs().QueueCollectionChronologySync(default, default!, default);
    }
}
