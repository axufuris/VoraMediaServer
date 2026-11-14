using Vora.Application.Analysis;
using Vora.Application.Collections;
using Vora.Application.Collections.Requests;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Collections;

public class CollectionManagerArtworkTests
{
    private readonly ICollectionRepository _repo = Substitute.For<ICollectionRepository>();
    private readonly ITaskQueueManager _tasks = Substitute.For<ITaskQueueManager>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();

    private CollectionManager Manager() => new(_repo, _tasks, _notifier, Substitute.For<IServerCalendar>());

    private Collection Existing()
    {
        var collection = new Collection { Title = "Christmas Classics", PosterUrl = "/api/artwork/vora/holidays/christmas.jpg?v=def", ContentSyncProviderId = "openai_theme", ContentSyncExternalId = "Christmas" };
        _repo.GetForUpdateAsync(collection.Id).Returns(collection);
        return collection;
    }

    [Fact]
    public async Task Setting_the_poster_saves_only_the_poster_and_tells_clients()
    {
        var collection = Existing();

        await Manager().SetArtworkAsync(collection.Id, ArtworkKind.Poster, " /api/artwork/custom/coll_poster.jpg ");

        collection.PosterUrl.Should().Be("/api/artwork/custom/coll_poster.jpg");
        collection.BackdropUrl.Should().BeNull();
        await _repo.Received(1).UpdateCollectionAsync(collection);
        await _notifier.Received(1).NotifyCollectionUpdatedAsync(collection.Id);
        _tasks.DidNotReceiveWithAnyArgs().QueueFullCollectionSync(default, default!, default, default);
    }

    [Fact]
    public async Task Setting_a_backdrop_leaves_the_poster_alone()
    {
        var collection = Existing();

        await Manager().SetArtworkAsync(collection.Id, ArtworkKind.Backdrop, "/api/artwork/custom/coll_backdrop.jpg");

        collection.BackdropUrl.Should().Be("/api/artwork/custom/coll_backdrop.jpg");
        collection.PosterUrl.Should().Be("/api/artwork/vora/holidays/christmas.jpg?v=def");
    }

    [Fact]
    public async Task An_empty_address_clears_the_artwork()
    {
        var collection = Existing();

        await Manager().SetArtworkAsync(collection.Id, ArtworkKind.Poster, "");

        collection.PosterUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_collection_has_no_logo()
    {
        var collection = Existing();

        var act = () => Manager().SetArtworkAsync(collection.Id, ArtworkKind.Logo, "/api/artwork/custom/logo.png");

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdateCollectionAsync(default!);
    }

    [Fact]
    public async Task A_missing_collection_is_refused()
    {
        var act = () => Manager().SetArtworkAsync(Guid.NewGuid(), ArtworkKind.Poster, "/api/artwork/custom/poster.jpg");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_new_collection_keeps_the_locks_its_artwork_picks_set()
    {
        Collection? created = null;
        _repo.CreateCollectionAsync(Arg.Do<Collection>(c => created = c)).Returns(Guid.NewGuid());

        await Manager().CreateCollectionAsync(new CreateCollectionRequest { Title = "Star Wars", PosterUrl = "https://image.tmdb.org/t/p/original/poster.jpg", LockedFields = ["PosterUrl"] });

        created.Should().NotBeNull();
        created!.LockedFields.Should().Equal("PosterUrl");
    }
}
