using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Tests.Watchlist;

public class WatchlistManagerTests
{
    private const string Tmdb = "tmdb_discovery";

    private readonly IWatchlistRepository _repo = Substitute.For<IWatchlistRepository>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly WatchlistManager _manager;

    public WatchlistManagerTests()
    {
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, Guid>());
        _manager = new WatchlistManager(_repo, _media, NullLogger<WatchlistManager>.Instance);
    }

    private static WatchlistRequest External(string externalId = "603") => new()
    {
        ExternalId = externalId,
        ProviderId = Tmdb,
        Type = "Movie",
        Title = "The Matrix",
    };

    [Fact]
    public async Task Toggle_adds_an_external_title_when_absent()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns((UserWatchlistItem?)null);

        var added = await _manager.ToggleAsync(profileId, External());

        added.Should().BeTrue();
        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.ProfileId == profileId && w.ExternalId == "603" && w.ProviderId == Tmdb && w.MediaItemId == null));
    }

    [Fact]
    public async Task Toggle_removes_when_already_present()
    {
        var profileId = Guid.NewGuid();
        var existing = new UserWatchlistItem { ExternalId = "603", ProviderId = Tmdb };
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns(existing);

        var added = await _manager.ToggleAsync(profileId, External());

        added.Should().BeFalse();
        await _repo.Received(1).RemoveAsync(existing);
        await _repo.DidNotReceive().AddAsync(Arg.Any<UserWatchlistItem>());
    }

    // The point of keying on TMDB: a library item and a Discovery entry for the
    // same title are one row, not two.
    [Fact]
    public async Task Toggling_a_library_item_reuses_its_tmdb_identity()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _media.GetTmdbIdAsync(mediaId).Returns("603");
        _repo.FindAsync(profileId, "603", Tmdb, mediaId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = mediaId, Type = "Movie", Title = "The Matrix" });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.ExternalId == "603" && w.ProviderId == Tmdb && w.MediaItemId == mediaId));
    }

    [Fact]
    public async Task Toggling_a_library_item_added_from_discovery_removes_the_same_row()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var fromDiscovery = new UserWatchlistItem { ExternalId = "603", ProviderId = Tmdb };
        _media.GetTmdbIdAsync(mediaId).Returns("603");
        _repo.FindAsync(profileId, "603", Tmdb, mediaId).Returns(fromDiscovery);

        var added = await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = mediaId, Type = "Movie", Title = "The Matrix" });

        added.Should().BeFalse();
        await _repo.Received(1).RemoveAsync(fromDiscovery);
    }

    // A home video or unmatched file has no TMDB id, so its own id is the key.
    [Fact]
    public async Task Toggling_a_library_item_without_a_tmdb_id_keys_on_the_media_id()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _media.GetTmdbIdAsync(mediaId).Returns((string?)null);
        _repo.FindAsync(profileId, null, null, mediaId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = mediaId, Type = "Movie", Title = "Holiday 2019" });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.MediaItemId == mediaId && w.ExternalId == string.Empty && w.ProviderId == string.Empty));
    }

    [Fact]
    public async Task Adding_an_external_title_already_in_the_library_records_the_local_id()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), "Movie")
            .Returns(new Dictionary<string, Guid> { ["603"] = mediaId });
        _repo.FindAsync(profileId, "603", Tmdb, mediaId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, External());

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w => w.MediaItemId == mediaId));
    }

    [Fact]
    public async Task Toggle_propagates_repository_exceptions()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns((UserWatchlistItem?)null);
        _repo.When(r => r.AddAsync(Arg.Any<UserWatchlistItem>()))
             .Do(_ => throw new InvalidOperationException("DB went sideways"));

        var act = () => _manager.ToggleAsync(profileId, External());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IsInWatchlist_matches_a_library_item_by_its_tmdb_identity()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _media.GetTmdbIdAsync(mediaId).Returns("603");
        _repo.FindAsync(profileId, "603", Tmdb, mediaId).Returns(new UserWatchlistItem());

        var result = await _manager.IsInWatchlistAsync(profileId, null, null, mediaId);

        result.Should().BeTrue();
    }

    // Entries saved before a title was acquired carry no MediaItemId; the read
    // resolves it so the client links to the local copy.
    [Fact]
    public async Task GetWatchlist_resolves_a_local_id_for_older_external_entries()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(new List<UserWatchlistItem>
        {
            new() { Id = Guid.NewGuid(), ProfileId = profileId, ExternalId = "603", ProviderId = Tmdb, Type = "Movie", Title = "The Matrix" },
        });
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), "Movie")
            .Returns(new Dictionary<string, Guid> { ["603"] = mediaId });

        var result = await _manager.GetWatchlistAsync(profileId);

        result.Should().ContainSingle().Which.MediaItemId.Should().Be(mediaId);
    }

    [Fact]
    public async Task GetWatchlist_leaves_MediaItemId_null_when_the_title_is_not_owned()
    {
        var profileId = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(new List<UserWatchlistItem>
        {
            new() { Id = Guid.NewGuid(), ProfileId = profileId, ExternalId = "999", ProviderId = Tmdb, Type = "Movie", Title = "Unowned" },
        });

        var result = await _manager.GetWatchlistAsync(profileId);

        result.Should().ContainSingle().Which.MediaItemId.Should().BeNull();
    }
}
