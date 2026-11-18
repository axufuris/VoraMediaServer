using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Dtos;
using Vora.Application.Notifications;
using Vora.Application.Tasks;
using Vora.Application.Tasks.Dtos;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Tasks;

public class TimelineSyncTaskTests
{
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly IAdminNotificationManager _admin = Substitute.For<IAdminNotificationManager>();
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly TaskQueueManager _queue;
    private readonly IServiceProvider _services;

    public TimelineSyncTaskTests()
    {
        _queue = new TaskQueueManager(_notifier, Substitute.For<ITaskJournal>());
        var ordering = new CollectionOrderingService(_repo, Substitute.For<Vora.Application.Media.IMediaRepository>(), new List<IChronologyProvider>(), _notifier, NullLogger<CollectionOrderingService>.Instance);
        _services = new ServiceCollection()
            .AddSingleton(ordering)
            .AddSingleton(_admin)
            .AddSingleton(_notifier)
            .BuildServiceProvider();
    }

    private async Task<QueuedTaskDto> NextTaskAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            return task;
        }

        throw new InvalidOperationException("Nothing was queued.");
    }

    [Fact]
    public async Task A_forced_sync_that_fails_raises_an_admin_alert_and_tells_the_open_page()
    {
        var id = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(id).Returns(new CollectionChronologyConfigDto { Title = "Star Wars Universe", SortProviderId = "gone" });
        _queue.QueueCollectionChronologySync(id, "Star Wars Universe", force: true);
        var task = await NextTaskAsync();

        var run = () => task.WorkItem(CancellationToken.None, _services);

        await run.Should().ThrowAsync<InvalidOperationException>();
        await _admin.Received(1).RaiseAsync(AdminNotificationSeverity.Warning, "'Star Wars Universe' could not be put in story order", Arg.Is<string>(m => m.Contains("gone")), Arg.Any<string?>(), Arg.Any<bool>());
        await _notifier.Received(1).NotifyCollectionUpdatedAsync(id);
    }

    [Fact]
    public async Task A_scheduled_sync_that_fails_stays_quiet_and_is_left_to_the_task_log()
    {
        var id = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(id).Returns(new CollectionChronologyConfigDto { Title = "Star Wars Universe", SortProviderId = "gone" });
        _queue.QueueCollectionChronologySync(id, "Star Wars Universe");
        var task = await NextTaskAsync();

        var run = () => task.WorkItem(CancellationToken.None, _services);

        await run.Should().ThrowAsync<InvalidOperationException>();
        await _admin.DidNotReceiveWithAnyArgs().RaiseAsync(default, default!, default!, default, default);
    }
}
