using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Collections;
using Vora.Domain.Entities.Library;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Collections;

public class CollectionVisibilityTests
{
    [Theory]
    [InlineData(true, 1, 3, CollectionHiddenReason.BelowMinimumSize)]
    [InlineData(true, 3, 3, null)]
    [InlineData(true, 10, 0, CollectionHiddenReason.AutomaticCollectionsHidden)]
    [InlineData(false, 1, 3, null)]
    [InlineData(false, 1, 0, null)]
    [InlineData(false, 0, 1, CollectionHiddenReason.Empty)]
    [InlineData(true, 0, 1, CollectionHiddenReason.Empty)]
    public void Only_automatic_collections_answer_to_the_library_minimum(bool systemGenerated, int items, int minimum, CollectionHiddenReason? expected)
    {
        CollectionVisibility.HiddenReason(systemGenerated, items, minimum).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 3, 3)]
    [InlineData(null, 7, 7)]
    [InlineData(0, 3, 0)]
    [InlineData(-4, 3, 0)]
    [InlineData(99, 3, 25)]
    public void A_missing_minimum_keeps_the_fallback_and_a_given_one_is_clamped(int? requested, int fallback, int expected)
    {
        CollectionVisibility.Clamp(requested, fallback).Should().Be(expected);
    }
}

public class CollectionDescriptionServiceTests
{
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly IMetadataProvider _tmdb = Substitute.For<IMetadataProvider>();
    private readonly Guid _library = Guid.NewGuid();

    private CollectionDescriptionService Service() => new(_repo, new[] { _tmdb }, NullLogger<CollectionDescriptionService>.Instance);

    [Fact]
    public async Task A_collection_the_provider_has_no_description_for_is_marked_so_it_is_not_asked_again()
    {
        var bourne = new CollectionDescriptionTarget(Guid.NewGuid(), 31562);
        var alien = new CollectionDescriptionTarget(Guid.NewGuid(), 8091);
        _repo.GetCollectionsAwaitingDescriptionAsync(_library).Returns(new List<CollectionDescriptionTarget> { bourne, alien });
        _tmdb.FetchCollectionOverviewAsync(31562, Arg.Any<CancellationToken>()).Returns("Jason Bourne's story.");
        _tmdb.FetchCollectionOverviewAsync(8091, Arg.Any<CancellationToken>()).Returns(string.Empty);

        var filled = await Service().FillAwaitingAsync(_library, TestContext.Current.CancellationToken);

        filled.Should().Be(2);
        await _repo.Received(1).UpdateDescriptionAsync(bourne.CollectionId, "Jason Bourne's story.");
        await _repo.Received(1).UpdateDescriptionAsync(alien.CollectionId, string.Empty);
    }

    [Fact]
    public async Task A_failed_request_leaves_the_collection_to_be_asked_on_a_later_scan()
    {
        var target = new CollectionDescriptionTarget(Guid.NewGuid(), 1);
        _repo.GetCollectionsAwaitingDescriptionAsync(_library).Returns(new List<CollectionDescriptionTarget> { target });
        _tmdb.FetchCollectionOverviewAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);

        (await Service().FillAwaitingAsync(_library, TestContext.Current.CancellationToken)).Should().Be(0);

        await _repo.DidNotReceive().UpdateDescriptionAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Refreshing_asks_again_even_after_none_was_found()
    {
        var collection = new Collection { Title = "Alien Collection", TmdbId = 8091, Description = string.Empty };
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);
        _tmdb.FetchCollectionOverviewAsync(8091, Arg.Any<CancellationToken>()).Returns("In space, no one can hear you scream.");

        var description = await Service().RefreshAsync(collection.Id, TestContext.Current.CancellationToken);

        description.Should().Be("In space, no one can hear you scream.");
        await _repo.Received(1).UpdateDescriptionAsync(collection.Id, "In space, no one can hear you scream.");
    }

    [Fact]
    public async Task Refreshing_leaves_a_locked_description_alone()
    {
        var collection = new Collection { Title = "Bourne", TmdbId = 31562, Description = "My own words." };
        collection.LockField(nameof(Collection.Description));
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);

        (await Service().RefreshAsync(collection.Id, TestContext.Current.CancellationToken)).Should().Be("My own words.");

        await _tmdb.DidNotReceive().FetchCollectionOverviewAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
