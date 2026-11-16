using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Tests.Watchlist;

public class WatchlistManagerTests
{
    private const string Tmdb = "tmdb_discovery";

    private readonly IWatchlistRepository _repo = Substitute.For<IWatchlistRepository>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly WatchlistManager _manager;

    public WatchlistManagerTests()
    {
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, Guid>());
        _media.GetPosterUrlsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, string>());
        _manager = new WatchlistManager(_repo, _media, _notifier, NullLogger<WatchlistManager>.Instance);
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

    // Every device on the profile re-reads its watchlist on WatchlistUpdated, so
    // a title taken off on one is gone from the others too.
    [Fact]
    public async Task Adding_tells_the_profile_its_watchlist_changed()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, External());

        await _notifier.Received(1).NotifyWatchlistUpdatedAsync(profileId);
    }

    [Fact]
    public async Task Removing_tells_the_profile_its_watchlist_changed()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns(new UserWatchlistItem { ExternalId = "603", ProviderId = Tmdb });

        await _manager.ToggleAsync(profileId, External());

        await _notifier.Received(1).NotifyWatchlistUpdatedAsync(profileId);
    }

    [Fact]
    public async Task A_change_that_fails_to_save_tells_no_one()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns((UserWatchlistItem?)null);
        _repo.AddAsync(Arg.Any<UserWatchlistItem>()).Returns(Task.FromException(new InvalidOperationException("db down")));

        var toggle = () => _manager.ToggleAsync(profileId, External());

        await toggle.Should().ThrowAsync<InvalidOperationException>();
        await _notifier.DidNotReceive().NotifyWatchlistUpdatedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_saved_change_is_reported_even_when_the_push_fails()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "603", Tmdb, null).Returns(new UserWatchlistItem { ExternalId = "603", ProviderId = Tmdb });
        _notifier.NotifyWatchlistUpdatedAsync(profileId).Returns(Task.FromException(new InvalidOperationException("hub down")));

        var added = await _manager.ToggleAsync(profileId, External());

        added.Should().BeFalse();
        await _repo.Received(1).RemoveAsync(Arg.Any<UserWatchlistItem>());
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

    [Fact]
    public async Task GetWatchlist_shows_a_library_title_with_its_current_poster_not_the_one_saved_when_added()
    {
        var profileId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var resolvedId = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(new List<UserWatchlistItem>
        {
            new() { Id = Guid.NewGuid(), ProfileId = profileId, MediaItemId = mediaId, Type = "Movie", Title = "Pitch Perfect 2", PosterUrl = "/api/artwork/custom/pp2_overlay_old.jpg" },
            new() { Id = Guid.NewGuid(), ProfileId = profileId, ExternalId = "603", ProviderId = Tmdb, Type = "Movie", Title = "The Matrix", PosterUrl = "https://image.tmdb.org/t/p/w500/matrix.jpg" },
        });
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), "Movie")
            .Returns(new Dictionary<string, Guid> { ["603"] = resolvedId });
        _media.GetPosterUrlsByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(mediaId) && ids.Contains(resolvedId)))
            .Returns(new Dictionary<Guid, string>
            {
                [mediaId] = "/api/artwork/custom/pp2_overlay_new.jpg",
                [resolvedId] = "/api/artwork/custom/matrix_overlay.jpg",
            });

        var result = await _manager.GetWatchlistAsync(profileId);

        result.Select(r => r.PosterUrl).Should().Equal("/api/artwork/custom/pp2_overlay_new.jpg", "/api/artwork/custom/matrix_overlay.jpg");
    }

    [Fact]
    public async Task GetWatchlist_keeps_the_saved_poster_when_the_title_is_not_in_the_library_or_has_none()
    {
        var profileId = Guid.NewGuid();
        var posterless = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(new List<UserWatchlistItem>
        {
            new() { Id = Guid.NewGuid(), ProfileId = profileId, ExternalId = "999", ProviderId = Tmdb, Type = "Movie", Title = "Unowned", PosterUrl = "https://image.tmdb.org/t/p/w500/unowned.jpg" },
            new() { Id = Guid.NewGuid(), ProfileId = profileId, MediaItemId = posterless, Type = "Movie", Title = "Holiday 2019", PosterUrl = "/api/artwork/custom/holiday.jpg" },
        });

        var result = await _manager.GetWatchlistAsync(profileId);

        result.Select(r => r.PosterUrl).Should().Equal("https://image.tmdb.org/t/p/w500/unowned.jpg", "/api/artwork/custom/holiday.jpg");
    }

    // --- A finished title leaves the watchlist

    private static readonly Guid ShowId = Guid.NewGuid();

    private void LibraryTitle(Guid titleId, string type, string? tmdbId)
    {
        _repo.GetTitleAsync(Arg.Any<Guid>()).Returns(new WatchlistTitle(titleId, type));
        _media.GetTmdbIdAsync(titleId).Returns(tmdbId);
    }

    [Fact]
    public async Task A_finished_movie_comes_off_the_watchlist_and_every_device_is_told()
    {
        var profileId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var entry = new UserWatchlistItem { ExternalId = "603", ProviderId = Tmdb, Type = "Movie" };
        LibraryTitle(movieId, "Movie", "603");
        _repo.FindAsync(profileId, "603", Tmdb, movieId).Returns(entry);
        _repo.IsFinishedAsync(profileId, movieId).Returns(true);

        var removed = await _manager.RemoveIfFinishedAsync(profileId, movieId);

        removed.Should().BeTrue();
        await _repo.Received(1).RemoveAsync(entry);
        await _notifier.Received(1).NotifyWatchlistUpdatedAsync(profileId);
    }

    [Fact]
    public async Task The_last_episode_of_a_show_takes_the_show_off_the_watchlist()
    {
        var profileId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var entry = new UserWatchlistItem { ExternalId = "1396", ProviderId = Tmdb, Type = "TvShow" };
        _repo.GetTitleAsync(episodeId).Returns(new WatchlistTitle(ShowId, "TvShow"));
        _media.GetTmdbIdAsync(ShowId).Returns("1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns(entry);
        _repo.IsFinishedAsync(profileId, ShowId).Returns(true);

        (await _manager.RemoveIfFinishedAsync(profileId, episodeId)).Should().BeTrue();

        await _repo.Received(1).RemoveAsync(entry);
    }

    [Fact]
    public async Task A_show_with_episodes_left_stays_on_the_watchlist()
    {
        var profileId = Guid.NewGuid();
        var entry = new UserWatchlistItem { ExternalId = "1396", ProviderId = Tmdb, Type = "TvShow" };
        LibraryTitle(ShowId, "TvShow", "1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns(entry);
        _repo.IsFinishedAsync(profileId, ShowId).Returns(false);

        (await _manager.RemoveIfFinishedAsync(profileId, Guid.NewGuid())).Should().BeFalse();

        await _repo.DidNotReceive().RemoveAsync(Arg.Any<UserWatchlistItem>());
        await _notifier.DidNotReceive().NotifyWatchlistUpdatedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_title_that_was_never_on_the_watchlist_costs_nothing_more()
    {
        var profileId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        LibraryTitle(movieId, "Movie", "603");
        _repo.FindAsync(profileId, "603", Tmdb, movieId).Returns((UserWatchlistItem?)null);

        (await _manager.RemoveIfFinishedAsync(profileId, movieId)).Should().BeFalse();

        await _repo.DidNotReceive().IsFinishedAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await _notifier.DidNotReceive().NotifyWatchlistUpdatedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_movie_and_a_show_with_the_same_tmdb_id_are_not_each_other()
    {
        var profileId = Guid.NewGuid();
        var movieEntry = new UserWatchlistItem { ExternalId = "1396", ProviderId = Tmdb, Type = "Movie" };
        LibraryTitle(ShowId, "TvShow", "1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns(movieEntry);
        _repo.IsFinishedAsync(profileId, ShowId).Returns(true);

        (await _manager.RemoveIfFinishedAsync(profileId, Guid.NewGuid())).Should().BeFalse();

        await _repo.DidNotReceive().RemoveAsync(Arg.Any<UserWatchlistItem>());
    }

    [Fact]
    public async Task Something_a_watchlist_cannot_hold_is_ignored()
    {
        _repo.GetTitleAsync(Arg.Any<Guid>()).Returns((WatchlistTitle?)null);

        (await _manager.RemoveIfFinishedAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();

        await _repo.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task A_failure_checking_the_watchlist_does_not_reach_the_caller()
    {
        _repo.GetTitleAsync(Arg.Any<Guid>()).Returns<WatchlistTitle?>(_ => throw new InvalidOperationException("database is down"));

        var removed = await _manager.RemoveIfFinishedAsync(Guid.NewGuid(), Guid.NewGuid());

        removed.Should().BeFalse();
        await _notifier.DidNotReceive().NotifyWatchlistUpdatedAsync(Arg.Any<Guid>());
    }
}
