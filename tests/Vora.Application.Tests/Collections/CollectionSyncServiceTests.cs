using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Notifications;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Collections;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Collections;

public class CollectionSyncServiceTests
{
    // NOTE: SyncCollectionContentAsync reads collection metadata via a private
    // anonymous-type projection. NSubstitute returns null for unstubbed generic
    // calls of that shape, so we can only verify the early-return path and the
    // surrounding wiring; the provider-match + reconciliation deeper in the
    // method needs the projection to return a non-null instance, which we can't
    // construct from outside the production assembly.

    private readonly ICollectionRepository _collectionRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IClientNotifier _notifier;
    private readonly ITaskQueueManager _taskQueue;
    private readonly IAdminNotificationManager _adminNotifications;
    private readonly List<ICollectionSyncProvider> _providers;
    private readonly CollectionSyncService _service;

    public CollectionSyncServiceTests()
    {
        _collectionRepo = Substitute.For<ICollectionRepository>();
        _mediaRepo = Substitute.For<IMediaRepository>();
        _notifier = Substitute.For<IClientNotifier>();
        _taskQueue = Substitute.For<ITaskQueueManager>();
        _adminNotifications = Substitute.For<IAdminNotificationManager>();
        _providers = new List<ICollectionSyncProvider>();
        _service = new CollectionSyncService(_collectionRepo, _mediaRepo, _providers, _notifier, _taskQueue,
            _adminNotifications, NullLogger<CollectionSyncService>.Instance);
    }

    [Fact]
    public async Task SyncCollectionContentAsync_returns_early_when_config_projection_is_null()
    {
        var collectionId = Guid.NewGuid();

        await _service.SyncCollectionContentAsync(collectionId);

        await _collectionRepo.DidNotReceive().GetCollectionMediaIdsAsync(Arg.Any<Guid>());
        await _collectionRepo.DidNotReceive().AddItemsToCollectionAsync(Arg.Any<List<CollectionItem>>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
        await _mediaRepo.DidNotReceiveWithAnyArgs().GetMediaIdsByExternalIdsAsync(
            Arg.Any<List<string>>(), Arg.Any<List<string>>());
    }

    [Fact]
    public async Task SyncCollectionContentAsync_does_not_throw_when_no_providers_registered()
    {
        var act = async () => await _service.SyncCollectionContentAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }
}
