using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Actors;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Metadata;

public class MetadataManagerTests
{
    // NOTE: most Trigger*MediaItem* paths read item-type via anonymous-type
    // projections (`GetProjectedAsync(id, m => new { m.Id, Type = m.GetType().Name })`),
    // which NSubstitute can't stub across assemblies. Tests focus on the actor-refresh
    // path which goes through normal interfaces, and on the library-level routing.

    private readonly IMediaRepository _media;
    private readonly IActorRepository _actors;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClientNotifier _notifier;
    private readonly IMetadataFetchService _fetch;
    private readonly IMetadataMappingService _mapping;
    private readonly MetadataManager _manager;

    public MetadataManagerTests()
    {
        _media = Substitute.For<IMediaRepository>();
        _actors = Substitute.For<IActorRepository>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _notifier = Substitute.For<IClientNotifier>();
        _fetch = Substitute.For<IMetadataFetchService>();
        _mapping = Substitute.For<IMetadataMappingService>();

        _media.GetDisplayTitlesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, string>());

        _manager = new MetadataManager(
            _media, _actors, _scopeFactory, _notifier, _fetch, _mapping,
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
            NullLogger<MetadataManager>.Instance);
    }

    private static Actor MakeActor(Guid id, int tmdbId = 1, bool isCustom = false, string name = "Anonymous") => new()
    {
        Id = id,
        Name = name,
        TmdbId = tmdbId,
        IsCustom = isCustom
    };

    [Fact]
    public async Task TriggerActorMetadataRefreshAsync_skips_custom_actors()
    {
        var customId = Guid.NewGuid();
        _actors.GetActorIdsMissingMetadataAsync(Arg.Any<int>()).Returns(new[] { customId });
        _actors.GetActorByIdAsync(customId).Returns(MakeActor(customId, isCustom: true));

        await _manager.TriggerActorMetadataRefreshAsync();

        await _fetch.DidNotReceiveWithAnyArgs().GetActorMetadataAsync(default);
        await _actors.DidNotReceiveWithAnyArgs().UpdateActorAsync(Arg.Any<Actor>());
    }

    [Fact]
    public async Task TriggerActorMetadataRefreshAsync_skips_when_actor_missing()
    {
        var id = Guid.NewGuid();
        _actors.GetActorIdsMissingMetadataAsync(Arg.Any<int>()).Returns(new[] { id });
        _actors.GetActorByIdAsync(id).Returns((Actor?)null);

        await _manager.TriggerActorMetadataRefreshAsync();

        await _fetch.DidNotReceiveWithAnyArgs().GetActorMetadataAsync(default);
    }

    [Fact]
    public async Task TriggerActorMetadataRefreshAsync_skips_when_provider_returns_null()
    {
        var id = Guid.NewGuid();
        _actors.GetActorIdsMissingMetadataAsync(Arg.Any<int>()).Returns(new[] { id });
        _actors.GetActorByIdAsync(id).Returns(MakeActor(id));
        _fetch.GetActorMetadataAsync(Arg.Any<int>()).Returns((ActorMetadataResult?)null);

        await _manager.TriggerActorMetadataRefreshAsync();

        await _actors.DidNotReceiveWithAnyArgs().UpdateActorAsync(Arg.Any<Actor>());
    }

    [Fact]
    public async Task TriggerActorMetadataRefreshAsync_applies_metadata_when_present()
    {
        var id = Guid.NewGuid();
        var actor = MakeActor(id, tmdbId: 42);
        _actors.GetActorIdsMissingMetadataAsync(Arg.Any<int>()).Returns(new[] { id });
        _actors.GetActorByIdAsync(id).Returns(actor);
        _fetch.GetActorMetadataAsync(42).Returns(new ActorMetadataResult
        {
            Biography = "bio",
            Birthday = new DateTime(1970, 1, 1),
            Deathday = null,
            PlaceOfBirth = "Earth",
            ImdbId = "nm0000001",
            HomePage = "https://example.com"
        });

        await _manager.TriggerActorMetadataRefreshAsync();

        actor.Biography.Should().Be("bio");
        actor.Birthday.Should().Be(new DateTime(1970, 1, 1));
        actor.PlaceOfBirth.Should().Be("Earth");
        actor.ImdbId.Should().Be("nm0000001");
        actor.HomePage.Should().Be("https://example.com");
        await _actors.Received(1).UpdateActorAsync(actor);
    }

    [Fact]
    public async Task TriggerActorMetadataRefreshAsync_continues_on_per_actor_exception()
    {
        var goodId = Guid.NewGuid();
        var badId = Guid.NewGuid();

        _actors.GetActorIdsMissingMetadataAsync(Arg.Any<int>()).Returns(new[] { badId, goodId });
        _actors.GetActorByIdAsync(badId).Returns<Actor?>(_ => throw new InvalidOperationException("transient"));
        _actors.GetActorByIdAsync(goodId).Returns(MakeActor(goodId, tmdbId: 7));
        _fetch.GetActorMetadataAsync(7).Returns(new ActorMetadataResult { Biography = "ok" });

        await _manager.TriggerActorMetadataRefreshAsync();

        // The good actor should still get processed despite the bad one throwing.
        await _actors.Received(1).UpdateActorAsync(Arg.Is<Actor>(a => a.Id == goodId));
    }

    [Fact]
    public async Task TriggerLibraryArtworkRefreshAsync_notifies_library_updated_at_end_even_when_no_items()
    {
        var libId = Guid.NewGuid();
        _media.GetAllProjectedAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Vora.Domain.Entities.Media.MediaItem, Guid>>>(),
            libraryId: libId)
            .Returns(Array.Empty<Guid>());

        await _manager.TriggerLibraryArtworkRefreshAsync(libId);

        await _notifier.Received().NotifyLibraryUpdatedAsync(libId);
    }

    [Fact]
    public async Task TriggerLibraryRatingsRefreshAsync_calls_repository_for_each_id_and_notifies()
    {
        var libId = Guid.NewGuid();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        // Non-force ratings refresh targets only items still missing a rating.
        _media.GetMediaIdsMissingRatingsAsync(libId).Returns(new[] { idA, idB });
        // GetForMetadataSyncAsync returns null for both → RefreshRatingsAsync no-ops
        _media.GetForMetadataSyncAsync(Arg.Any<Guid>()).Returns((Vora.Domain.Entities.Media.MediaItem?)null);

        await _manager.TriggerLibraryRatingsRefreshAsync(libId);

        await _media.Received(1).GetForMetadataSyncAsync(idA);
        await _media.Received(1).GetForMetadataSyncAsync(idB);
        await _notifier.Received().NotifyLibraryUpdatedAsync(libId);
    }
}
