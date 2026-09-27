using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Dtos;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Collections;

public class CollectionOrderingServiceTests
{
    private readonly ICollectionRepository _repo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IClientNotifier _notifier;
    private readonly CollectionOrderingService _service;
    private readonly List<IChronologyProvider> _providers;

    public CollectionOrderingServiceTests()
    {
        _repo = Substitute.For<ICollectionRepository>();
        _mediaRepo = Substitute.For<IMediaRepository>();
        _mediaRepo.GetSeasonShowInfoAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, SeasonShowInfoDto>());
        _notifier = Substitute.For<IClientNotifier>();
        _providers = new List<IChronologyProvider>();
        _service = new CollectionOrderingService(_repo, _mediaRepo, _providers, _notifier, NullLogger<CollectionOrderingService>.Instance);
    }

    [Fact]
    public async Task ApplyChronologicalOrderAsync_returns_early_when_config_is_null()
    {
        var collectionId = Guid.NewGuid();
        _repo.GetChronologyConfigAsync(collectionId).Returns((CollectionChronologyConfigDto?)null);

        await _service.ApplyChronologicalOrderAsync(collectionId, cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task ApplyChronologicalOrderAsync_skips_when_ai_signature_unchanged_and_not_forced()
    {
        var collectionId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var items = new List<CollectionItem> { new() { MediaItemId = mediaId, MediaItem = new Movie { Id = mediaId, Title = "M" } } };
        var signature = ComputeSignature(new[] { mediaId });

        _providers.Add(new FakeChronologyProvider("fake_chrono", ordersLocalItemsOnly: true));
        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "fake_chrono", ChronologyItemsSignature = signature });
        _repo.GetCollectionItemsWithMediaAsync(collectionId).Returns(items);

        await _service.ApplyChronologicalOrderAsync(collectionId, cancellationToken: TestContext.Current.CancellationToken);

        await _repo.Received(1).TouchChronologySyncedAtAsync(collectionId);
        await _repo.DidNotReceive().UpdateChronologySignatureAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ApplyChronologicalOrderAsync_reorders_when_forced_even_if_signature_unchanged()
    {
        var collectionId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var items = new List<CollectionItem> { new() { MediaItemId = mediaId, MediaItem = new Movie { Id = mediaId, Title = "M" } } };
        var signature = ComputeSignature(new[] { mediaId });

        _providers.Add(new FakeChronologyProvider("fake_chrono", ordersLocalItemsOnly: true));
        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "fake_chrono", ChronologyItemsSignature = signature });
        _repo.GetCollectionItemsWithMediaAsync(collectionId).Returns(items);

        await _service.ApplyChronologicalOrderAsync(collectionId, force: true, cancellationToken: TestContext.Current.CancellationToken);

        await _repo.Received(1).UpdateChronologySignatureAsync(collectionId, signature);
        await _repo.DidNotReceive().TouchChronologySyncedAtAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ApplyChronologicalOrderAsync_persists_in_universe_year_and_forwards_cached_year()
    {
        var collectionId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var item = new CollectionItem { MediaItemId = mediaId, MediaItem = new Movie { Id = mediaId, Title = "M" }, InUniverseYear = 1943 };
        var provider = new CapturingChronologyProvider();
        provider.Result.Add(new ChronologyResult { LocalId = mediaId, SortOrder = 5, SetYear = 1943 });
        _providers.Add(provider);

        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "capturing_chrono", ChronologyItemsSignature = "stale" });
        _repo.GetCollectionItemsWithMediaAsync(collectionId).Returns(new List<CollectionItem> { item });

        await _service.ApplyChronologicalOrderAsync(collectionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1943.0, provider.LastItems!.Single().KnownSetYear);
        Assert.Equal(5m, item.SortOrder);
        Assert.Equal(1943.0, item.InUniverseYear);
        await _repo.Received(1).UpdateCollectionItemsAsync(Arg.Any<IEnumerable<CollectionItem>>());
    }

    [Fact]
    public async Task ApplyChronologicalOrderAsync_force_ignores_the_cached_year()
    {
        var collectionId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var item = new CollectionItem { MediaItemId = mediaId, MediaItem = new Movie { Id = mediaId, Title = "M" }, InUniverseYear = 1943 };
        var provider = new CapturingChronologyProvider();
        provider.Result.Add(new ChronologyResult { LocalId = mediaId, SortOrder = 1, SetYear = 2012 });
        _providers.Add(provider);

        _repo.GetChronologyConfigAsync(collectionId)
            .Returns(new CollectionChronologyConfigDto { SortProviderId = "capturing_chrono", ChronologyItemsSignature = "whatever" });
        _repo.GetCollectionItemsWithMediaAsync(collectionId).Returns(new List<CollectionItem> { item });

        await _service.ApplyChronologicalOrderAsync(collectionId, force: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(provider.LastItems!.Single().KnownSetYear);
        Assert.Equal(2012.0, item.InUniverseYear);
    }

    private static string ComputeSignature(IEnumerable<Guid> mediaItemIds)
    {
        var joined = string.Join(",", mediaItemIds.OrderBy(x => x));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined)));
    }

    private sealed class CapturingChronologyProvider : IChronologyProvider
    {
        public IReadOnlyList<CollectionOrderingItemDto>? LastItems { get; private set; }
        public List<ChronologyResult> Result { get; } = new();

        public string Id => "capturing_chrono";
        public string Name => "Capturing";
        public string Version => "1.0.0";
        public string Description => "Captures the items it was given.";
        public bool IsSystemPlugin => true;
        public string Type => "Chronology";
        public string ProviderId => "capturing_chrono";
        public bool OrdersLocalItemsOnly => true;
        public string ExternalIdLabel => "List";
        public string ExternalIdPlaceholder => "id";

        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

        public Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
        {
            LastItems = items;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeChronologyProvider(string id, bool ordersLocalItemsOnly = false) : IChronologyProvider
    {
        public string Id => id;
        public string Name => "Fake";
        public string Version => "1.0.0";
        public string Description => "Fake chronology provider for tests.";
        public bool IsSystemPlugin => true;
        public string Type => "Chronology";
        public string ProviderId => id;
        public bool OrdersLocalItemsOnly => ordersLocalItemsOnly;
        public string ExternalIdLabel => "List";
        public string ExternalIdPlaceholder => "id";

        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

        public Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ChronologyResult>());
    }
}
