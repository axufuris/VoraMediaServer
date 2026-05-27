using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Collections;

public class CollectionOrderingServiceTests
{
    // NOTE: production code reads collection config via private anonymous types
    // (`GetProjectedByIdAsync(id, c => new { c.Title, c.SortProviderId, c.ExternalListId })`),
    // which NSubstitute can't stub across assemblies. Tests cover what we can hit
    // without that path: the DefaultSort gate and early-return when the anonymous
    // projection returns null.

    private readonly ICollectionRepository _repo;
    private readonly IClientNotifier _notifier;
    private readonly CollectionOrderingService _service;
    private readonly List<IChronologyProvider> _providers;

    public CollectionOrderingServiceTests()
    {
        _repo = Substitute.For<ICollectionRepository>();
        _notifier = Substitute.For<IClientNotifier>();
        _providers = new List<IChronologyProvider>();
        _service = new CollectionOrderingService(_repo, _providers, _notifier, NullLogger<CollectionOrderingService>.Instance);
    }

    [Fact]
    public async Task ApplyChronologicalOrderAsync_returns_early_when_config_projection_is_null()
    {
        // The anonymous-type projection returns null by default from NSubstitute.
        var collectionId = Guid.NewGuid();

        await _service.ApplyChronologicalOrderAsync(collectionId, TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().GetCollectionItemsWithMediaAsync(Arg.Any<Guid>());
        await _repo.DidNotReceive().UpdateCollectionItemsAsync(Arg.Any<IEnumerable<CollectionItem>>());
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_no_op_when_default_sort_is_not_chronological()
    {
        var collectionId = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionSortOrder>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(CollectionSortOrder.ReleaseDateAsc);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, Guid.NewGuid(), forceFullRefetch: true, providerId: "tmdb-collections", TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
        await _repo.DidNotReceive().GetCollectionItemsWithMediaAsync(Arg.Any<Guid>());
    }

    [Theory]
    [InlineData(CollectionSortOrder.Alphabetical)]
    [InlineData(CollectionSortOrder.DateAddedDesc)]
    [InlineData(CollectionSortOrder.ReleaseDateDesc)]
    public async Task ReevaluateOrderOnItemAddedAsync_no_op_for_non_chronological_default_sorts(CollectionSortOrder sort)
    {
        var collectionId = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionSortOrder>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(sort);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, Guid.NewGuid(), forceFullRefetch: true, providerId: "p", TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_skips_when_not_force_full_refetch_even_if_chronological()
    {
        var collectionId = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionSortOrder>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(CollectionSortOrder.Chronological);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, Guid.NewGuid(), forceFullRefetch: false, providerId: "p", TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_skips_when_provider_id_blank_even_if_chronological_and_forced()
    {
        var collectionId = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionSortOrder>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(CollectionSortOrder.Chronological);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, Guid.NewGuid(), forceFullRefetch: true, providerId: null, TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_notifies_when_chronological_force_refetch_and_provider_set()
    {
        // Note: the inner ApplyChronologicalOrderAsync also reads an anonymous-type projection
        // that returns null by default, so it returns early — but the notifier still fires.
        var collectionId = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(
            collectionId,
            Arg.Any<Expression<Func<Collection, CollectionSortOrder>>>(),
            Arg.Any<bool>(),
            Arg.Any<List<Guid>?>())
            .Returns(CollectionSortOrder.Chronological);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, Guid.NewGuid(), forceFullRefetch: true, providerId: "tmdb-collections", TestContext.Current.CancellationToken);

        await _notifier.Received(1).NotifyCollectionUpdatedAsync(collectionId);
    }
}
