using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Notifications;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;
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
    private readonly ISmartPlaylistEvaluator _smartEvaluator;
    private readonly List<ICollectionSyncProvider> _providers;
    private readonly CollectionSyncService _service;

    public CollectionSyncServiceTests()
    {
        _collectionRepo = Substitute.For<ICollectionRepository>();
        _mediaRepo = Substitute.For<IMediaRepository>();
        _notifier = Substitute.For<IClientNotifier>();
        _taskQueue = Substitute.For<ITaskQueueManager>();
        _adminNotifications = Substitute.For<IAdminNotificationManager>();
        _smartEvaluator = Substitute.For<ISmartPlaylistEvaluator>();
        _providers = new List<ICollectionSyncProvider>();
        _service = new CollectionSyncService(_collectionRepo, _mediaRepo, _providers, _smartEvaluator, _notifier, _taskQueue,
            _adminNotifications, NullLogger<CollectionSyncService>.Instance);
    }

    [Fact]
    public async Task SyncCollectionContentAsync_returns_early_when_config_projection_is_null()
    {
        var collectionId = Guid.NewGuid();

        await _service.SyncCollectionContentAsync(collectionId, TestContext.Current.CancellationToken);

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

    private ILibraryCollectionSyncProvider ThemeProvider(Guid collectionId, params Guid[] found)
    {
        var provider = Substitute.For<ICollectionSyncProvider, ILibraryCollectionSyncProvider>();
        provider.Id.Returns("openai_theme");
        var library = (ILibraryCollectionSyncProvider)provider;
        library.FindInLibraryAsync(Arg.Is<LibraryCollectionSyncRequest>(r => r.CollectionId == collectionId && r.Theme == "Halloween"), Arg.Any<CancellationToken>())
            .Returns(found);
        _providers.Add(provider);
        return library;
    }

    private void Stored(Guid collectionId, bool mirror, HashSet<Guid> existing, HashSet<Guid> excluded)
    {
        _collectionRepo.GetProjectedByIdAsync(collectionId, Arg.Any<System.Linq.Expressions.Expression<Func<Collection, CollectionSyncService.SyncTarget>>>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>())
            .Returns(new CollectionSyncService.SyncTarget(collectionId, "Halloween Movies", "Spooky.", "openai_theme", "Halloween", null, null, mirror, null));
        _collectionRepo.GetCollectionMediaIdsAsync(collectionId).Returns(existing);
        _collectionRepo.GetExcludedMediaIdsAsync(collectionId).Returns(excluded);
        _collectionRepo.GetManuallyAddedMediaIdsAsync(collectionId).Returns(new HashSet<Guid>());
    }

    [Fact]
    public async Task A_theme_collection_takes_what_its_provider_finds_in_the_library_less_what_was_taken_out()
    {
        var collectionId = Guid.NewGuid();
        var (hocusPocus, trickRTreat, saw) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var provider = ThemeProvider(collectionId, hocusPocus, trickRTreat, saw);
        Stored(collectionId, mirror: false, existing: new HashSet<Guid>(), excluded: new HashSet<Guid> { saw });

        await _service.SyncCollectionContentAsync(collectionId, TestContext.Current.CancellationToken);

        await _collectionRepo.Received(1).AddItemsToCollectionAsync(Arg.Is<List<CollectionItem>>(items =>
            items.Select(i => i.MediaItemId).OrderBy(id => id).SequenceEqual(new[] { hocusPocus, trickRTreat }.OrderBy(id => id))));
        await _collectionRepo.Received(1).TouchContentSyncedAtAsync(collectionId);
        await ((ICollectionSyncProvider)provider).DidNotReceive().FetchItemsAsync(Arg.Any<string>());
        _taskQueue.Received(1).QueueReevaluateCollectionOrder(collectionId);
    }

    [Fact]
    public async Task A_mirrored_theme_collection_drops_a_title_its_provider_no_longer_finds()
    {
        var collectionId = Guid.NewGuid();
        var (kept, dropped) = (Guid.NewGuid(), Guid.NewGuid());
        ThemeProvider(collectionId, kept);
        Stored(collectionId, mirror: true, existing: new HashSet<Guid> { kept, dropped }, excluded: new HashSet<Guid>());

        await _service.SyncCollectionContentAsync(collectionId, TestContext.Current.CancellationToken);

        await _collectionRepo.Received(1).RemoveItemsFromCollectionAsync(collectionId, Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == dropped));
        await _collectionRepo.DidNotReceive().AddItemsToCollectionAsync(Arg.Any<List<CollectionItem>>());
    }

    [Fact]
    public async Task A_theme_that_finds_nothing_for_an_empty_collection_tells_the_admin()
    {
        var collectionId = Guid.NewGuid();
        ThemeProvider(collectionId);
        Stored(collectionId, mirror: false, existing: new HashSet<Guid>(), excluded: new HashSet<Guid>());

        await _service.SyncCollectionContentAsync(collectionId, TestContext.Current.CancellationToken);

        await _adminNotifications.Received(1).RaiseAsync(AdminNotificationSeverity.Warning,
            Arg.Is<string>(title => title.Contains("found nothing")), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>());
        await _collectionRepo.Received(1).TouchContentSyncedAtAsync(collectionId);
        await _collectionRepo.DidNotReceive().AddItemsToCollectionAsync(Arg.Any<List<CollectionItem>>());
    }
}
