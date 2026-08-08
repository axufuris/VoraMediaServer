using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Dtos;
using Vora.Domain.Entities.Collections;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Collections;

public class CollectionOrderingServiceTests
{
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
    public async Task ApplyChronologicalOrderAsync_returns_early_when_config_is_null()
    {
        var collectionId = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(collectionId).Returns((CollectionChronologyConfigDto?)null);

        await _service.ApplyChronologicalOrderAsync(collectionId, TestContext.Current.CancellationToken);

        await _repo.DidNotReceive().GetCollectionItemsWithMediaAsync(Arg.Any<Guid>());
        await _repo.DidNotReceive().UpdateCollectionItemsAsync(Arg.Any<IEnumerable<CollectionItem>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ReevaluateOrderOnItemAddedAsync_no_op_when_no_sort_provider(string? providerId)
    {
        var collectionId = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = providerId });

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
        await _repo.DidNotReceive().GetCollectionItemsWithMediaAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_no_op_when_item_set_unchanged()
    {
        var collectionId = Guid.NewGuid();
        var mediaIds = new HashSet<Guid> { Guid.NewGuid() };
        var signature = ComputeSignature(mediaIds);

        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "fake_chrono", ChronologyItemsSignature = signature });
        _repo.GetCollectionMediaIdsAsync(collectionId).Returns(mediaIds);

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, TestContext.Current.CancellationToken);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyCollectionUpdatedAsync(default);
        await _repo.DidNotReceive().GetCollectionItemsWithMediaAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ReevaluateOrderOnItemAddedAsync_reapplies_and_notifies_when_item_set_changed()
    {
        var collectionId = Guid.NewGuid();
        _providers.Add(new FakeChronologyProvider("fake_chrono"));

        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "fake_chrono", ChronologyItemsSignature = "stale-signature" });
        _repo.GetCollectionMediaIdsAsync(collectionId).Returns(new HashSet<Guid> { Guid.NewGuid() });
        _repo.GetCollectionItemsWithMediaAsync(collectionId).Returns(new List<CollectionItem>());

        await _service.ReevaluateOrderOnItemAddedAsync(collectionId, TestContext.Current.CancellationToken);

        await _repo.Received(1).GetCollectionItemsWithMediaAsync(collectionId);
        await _notifier.Received(1).NotifyCollectionUpdatedAsync(collectionId);
    }

    private static string ComputeSignature(IEnumerable<Guid> mediaItemIds)
    {
        var joined = string.Join(",", mediaItemIds.OrderBy(x => x));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined)));
    }

    private sealed class FakeChronologyProvider(string id) : IChronologyProvider
    {
        public string Id => id;
        public string Name => "Fake";
        public string Version => "1.0.0";
        public string Description => "Fake chronology provider for tests.";
        public bool IsSystemPlugin => true;
        public string Type => "Chronology";
        public string ProviderId => id;
        public string ExternalIdLabel => "List";
        public string ExternalIdPlaceholder => "id";

        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

        public Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ChronologyResult>());
    }
}
