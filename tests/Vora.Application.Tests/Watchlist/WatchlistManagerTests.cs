using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Discovery;
using Vora.Application.Watchlist.ViewModels;
using DiscoveryItemDetailsVM = Vora.Application.Discovery.ViewModels.DiscoveryItemDetailsVM;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Watchlist;

public class WatchlistManagerTests
{
    private const string Tmdb = "tmdb_discovery";

    private readonly IWatchlistRepository _repo = Substitute.For<IWatchlistRepository>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IRequestRepository _requests = Substitute.For<IRequestRepository>();
    private readonly IDiscoveryManager _discovery = Substitute.For<IDiscoveryManager>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly WatchlistManager _manager;

    public WatchlistManagerTests()
    {
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, Guid>());
        _media.GetPosterUrlsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, string>());
        _media.GetReleaseDatesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, DateOnly>());
        _requests.GetRequestsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, MediaRequest>());
        _manager = new WatchlistManager(_repo, _media, _requests, _discovery, _notifier, NullLogger<WatchlistManager>.Instance);
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

    [Fact]
    public async Task Watchlisting_a_season_adds_its_show_under_the_shows_own_title_poster_and_type()
    {
        var profileId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        _repo.GetTitleAsync(seasonId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", "/api/artwork/custom/mobland.jpg"));
        _media.GetTmdbIdAsync(ShowId).Returns("1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns((UserWatchlistItem?)null);

        var added = await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = seasonId, Type = "Season", Title = "Season 1", PosterUrl = "/season.jpg" });

        added.Should().BeTrue();
        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.ProfileId == profileId && w.MediaItemId == ShowId && w.ExternalId == "1396" && w.ProviderId == Tmdb
            && w.Type == "TvShow" && w.Title == "Mobland" && w.PosterUrl == "/api/artwork/custom/mobland.jpg"));
    }

    [Fact]
    public async Task Watchlisting_an_episode_adds_its_show_and_not_the_episodes_release_date()
    {
        var profileId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _repo.GetTitleAsync(episodeId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", null));
        _media.GetTmdbIdAsync(ShowId).Returns("1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest
        {
            MediaItemId = episodeId,
            Type = "Episode",
            Title = "Pilot",
            ExpectedReleaseDate = new DateOnly(2026, 12, 25),
        });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.MediaItemId == ShowId && w.Title == "Mobland" && w.ExpectedReleaseDate == null));
    }

    [Fact]
    public async Task Watchlisting_a_season_of_a_show_already_on_the_watchlist_takes_the_show_off()
    {
        var profileId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var existing = new UserWatchlistItem { ExternalId = "1396", ProviderId = Tmdb, Type = "TvShow" };
        _repo.GetTitleAsync(seasonId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", null));
        _media.GetTmdbIdAsync(ShowId).Returns("1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns(existing);

        var added = await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = seasonId, Type = "Season", Title = "Season 1" });

        added.Should().BeFalse();
        await _repo.Received(1).RemoveAsync(existing);
        await _repo.DidNotReceive().AddAsync(Arg.Any<UserWatchlistItem>());
    }

    [Fact]
    public async Task A_season_and_an_episode_read_as_on_the_watchlist_when_their_show_is()
    {
        var profileId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _repo.GetTitleAsync(seasonId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", null));
        _repo.GetTitleAsync(episodeId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", null));
        _media.GetTmdbIdAsync(ShowId).Returns("1396");
        _repo.FindAsync(profileId, "1396", Tmdb, ShowId).Returns(new UserWatchlistItem());

        (await _manager.IsInWatchlistAsync(profileId, null, null, seasonId)).Should().BeTrue();
        (await _manager.IsInWatchlistAsync(profileId, null, null, episodeId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_show_with_no_provider_id_is_added_by_its_own_id_when_a_season_is_watchlisted()
    {
        var profileId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        _repo.GetTitleAsync(seasonId).Returns(new WatchlistTitle(ShowId, "TvShow", "Home Videos", null));
        _media.GetTmdbIdAsync(ShowId).Returns((string?)null);
        _repo.FindAsync(profileId, null, null, ShowId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = seasonId, Type = "Season", Title = "Season 1" });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.MediaItemId == ShowId && w.ExternalId == "" && w.ProviderId == "" && w.Type == "TvShow"));
    }

    [Fact]
    public async Task Watchlisting_a_movie_keeps_what_the_client_sent()
    {
        var profileId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        _repo.GetTitleAsync(movieId).Returns(new WatchlistTitle(movieId, "Movie", "The Matrix", null));
        _media.GetTmdbIdAsync(movieId).Returns("603");
        _repo.FindAsync(profileId, "603", Tmdb, movieId).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest { MediaItemId = movieId, Type = "Movie", Title = "The Matrix (sent)", PosterUrl = "/sent.jpg" });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.MediaItemId == movieId && w.Title == "The Matrix (sent)" && w.PosterUrl == "/sent.jpg" && w.Type == "Movie"));
    }

    private static UserWatchlistItem Entry(string externalId, DateOnly? stored = null, Guid? mediaItemId = null, string type = "Movie") => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = externalId,
        ProviderId = Tmdb,
        Type = type,
        Title = "A title " + externalId,
        ReleaseDate = stored,
        MediaItemId = mediaItemId,
    };

    private Task<List<WatchlistItemVM>> ReadAsync(params UserWatchlistItem[] entries)
    {
        var profileId = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(entries.ToList());
        return _manager.GetWatchlistAsync(profileId);
    }

    [Fact]
    public async Task A_title_in_the_library_carries_the_librarys_release_date_over_the_one_stored()
    {
        var libraryId = Guid.NewGuid();
        _media.GetReleaseDatesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new Dictionary<Guid, DateOnly> { [libraryId] = new DateOnly(1999, 3, 31) });

        var result = await ReadAsync(Entry("603", new DateOnly(1999, 1, 1), libraryId));

        result.Single().ReleaseDate.Should().Be(new DateOnly(1999, 3, 31));
        await _discovery.DidNotReceive().GetItemDetailsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_title_outside_the_library_carries_the_release_date_stored_with_it_and_is_not_looked_up()
    {
        var result = await ReadAsync(Entry("27205", new DateOnly(2010, 7, 16)));

        result.Single().ReleaseDate.Should().Be(new DateOnly(2010, 7, 16));
        await _discovery.DidNotReceive().GetItemDetailsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_title_with_no_release_date_is_looked_up_on_its_provider_and_the_answer_is_kept()
    {
        var entry = Entry("27205");
        _discovery.GetItemDetailsAsync(Tmdb, "27205", "Movie", Arg.Any<CancellationToken>())
            .Returns(new DiscoveryItemDetailsVM { ReleaseDate = new DateOnly(2010, 7, 16) });

        var result = await ReadAsync(entry);

        result.Single().ReleaseDate.Should().Be(new DateOnly(2010, 7, 16));
        await _repo.Received(1).SetReleaseDateAsync(entry.Id, new DateOnly(2010, 7, 16));
    }

    [Fact]
    public async Task A_lookup_that_finds_nothing_leaves_the_date_blank_and_is_not_asked_again_at_once()
    {
        var entry = Entry("404");
        var profileId = Guid.NewGuid();
        _repo.GetWatchlistAsync(profileId).Returns(new List<UserWatchlistItem> { entry });

        (await _manager.GetWatchlistAsync(profileId)).Single().ReleaseDate.Should().BeNull();
        (await _manager.GetWatchlistAsync(profileId)).Single().ReleaseDate.Should().BeNull();

        await _discovery.Received(1).GetItemDetailsAsync(Tmdb, "404", "Movie", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SetReleaseDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task A_lookup_that_fails_does_not_fail_the_read()
    {
        _discovery.GetItemDetailsAsync(Tmdb, "500", "Movie", Arg.Any<CancellationToken>())
            .Returns<DiscoveryItemDetailsVM?>(_ => throw new HttpRequestException("provider down"));

        var result = await ReadAsync(Entry("500"), Entry("501", new DateOnly(2001, 2, 3)));

        result.Select(r => r.ReleaseDate).Should().Equal(null, new DateOnly(2001, 2, 3));
    }

    [Fact]
    public async Task No_more_than_ten_titles_are_looked_up_in_one_read()
    {
        var entries = Enumerable.Range(0, 12).Select(n => Entry("9" + n)).ToArray();

        await ReadAsync(entries);

        await _discovery.Received(10).GetItemDetailsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_title_with_no_provider_entry_is_not_looked_up()
    {
        var entry = Entry("");
        entry.ProviderId = string.Empty;

        await ReadAsync(entry);

        await _discovery.DidNotReceive().GetItemDetailsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adding_from_discovery_keeps_the_release_date_the_client_sent()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "1396", Tmdb, null).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest
        {
            ExternalId = "1396",
            ProviderId = Tmdb,
            Type = "TvShow",
            Title = "MobLand",
            ReleaseDate = new DateOnly(2025, 3, 30),
            ExpectedReleaseDate = new DateOnly(2026, 12, 1),
        });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w =>
            w.ReleaseDate == new DateOnly(2025, 3, 30) && w.ExpectedReleaseDate == new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public async Task A_movie_added_without_a_release_date_takes_its_expected_date_for_it_and_a_show_does_not()
    {
        var profileId = Guid.NewGuid();
        _repo.FindAsync(profileId, "27205", Tmdb, null).Returns((UserWatchlistItem?)null);
        _repo.FindAsync(profileId, "1396", Tmdb, null).Returns((UserWatchlistItem?)null);

        await _manager.ToggleAsync(profileId, new WatchlistRequest { ExternalId = "27205", ProviderId = Tmdb, Type = "Movie", Title = "Inception", ExpectedReleaseDate = new DateOnly(2010, 7, 16) });
        await _manager.ToggleAsync(profileId, new WatchlistRequest { ExternalId = "1396", ProviderId = Tmdb, Type = "TvShow", Title = "MobLand", ExpectedReleaseDate = new DateOnly(2026, 12, 1) });

        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w => w.ExternalId == "27205" && w.ReleaseDate == new DateOnly(2010, 7, 16)));
        await _repo.Received(1).AddAsync(Arg.Is<UserWatchlistItem>(w => w.ExternalId == "1396" && w.ReleaseDate == null));
    }

    // --- A finished title leaves the watchlist

    private static readonly Guid ShowId = Guid.NewGuid();

    private void LibraryTitle(Guid titleId, string type, string? tmdbId)
    {
        _repo.GetTitleAsync(Arg.Any<Guid>()).Returns(new WatchlistTitle(titleId, type, "A title", null));
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
        _repo.GetTitleAsync(episodeId).Returns(new WatchlistTitle(ShowId, "TvShow", "Mobland", null));
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

    // --- The corner icon: a check for a title in the library, a clock for one
    // that is requested and hasn't arrived

    private static UserWatchlistItem Saved(string externalId, string type = "Movie") => new()
    {
        Id = Guid.NewGuid(), ProfileId = Guid.NewGuid(), ExternalId = externalId, ProviderId = Tmdb, Type = type, Title = "Title " + externalId,
    };

    private void Requested(string externalId, RequestStatus status, string type = "Movie") =>
        _requests.GetRequestsAsync(Arg.Is<IEnumerable<string>>(ids => ids.Contains(externalId)), type)
            .Returns(new Dictionary<string, MediaRequest> { [externalId] = new() { ExternalId = externalId, Type = type, Status = status } });

    [Theory]
    [InlineData(RequestStatus.Pending)]
    [InlineData(RequestStatus.Approved)]
    [InlineData(RequestStatus.Processing)]
    public async Task A_title_that_is_requested_and_not_in_a_library_is_marked_requested(RequestStatus status)
    {
        var entry = Saved("603");
        _repo.GetWatchlistAsync(entry.ProfileId).Returns([entry]);
        Requested("603", status);

        var list = await _manager.GetWatchlistAsync(entry.ProfileId);

        list.Should().ContainSingle().Which.IsRequested.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestStatus.Denied)]
    [InlineData(RequestStatus.Available)]
    public async Task A_denied_or_fulfilled_request_is_not_shown_as_requested(RequestStatus status)
    {
        var entry = Saved("603");
        _repo.GetWatchlistAsync(entry.ProfileId).Returns([entry]);
        Requested("603", status);

        (await _manager.GetWatchlistAsync(entry.ProfileId)).Should().ContainSingle().Which.IsRequested.Should().BeFalse();
    }

    [Fact]
    public async Task A_title_nobody_asked_for_is_not_marked()
    {
        var entry = Saved("603");
        _repo.GetWatchlistAsync(entry.ProfileId).Returns([entry]);

        (await _manager.GetWatchlistAsync(entry.ProfileId)).Should().ContainSingle().Which.IsRequested.Should().BeFalse();
    }

    [Fact]
    public async Task A_title_in_the_library_gets_its_check_and_no_request_is_looked_up_for_it()
    {
        var entry = Saved("603");
        var localId = Guid.NewGuid();
        _repo.GetWatchlistAsync(entry.ProfileId).Returns([entry]);
        _media.GetLocalIdsByExternalIdsAsync(Arg.Any<IEnumerable<string>>(), "Movie").Returns(new Dictionary<string, Guid> { ["603"] = localId });
        Requested("603", RequestStatus.Pending);

        var item = (await _manager.GetWatchlistAsync(entry.ProfileId)).Should().ContainSingle().Subject;

        item.MediaItemId.Should().Be(localId);
        item.IsRequested.Should().BeFalse();
        await _requests.DidNotReceive().GetRequestsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_movie_and_a_show_with_the_same_id_are_looked_up_by_their_own_type()
    {
        var movie = Saved("1396", "Movie");
        var show = Saved("1396", "TvShow");
        _repo.GetWatchlistAsync(movie.ProfileId).Returns([movie, show]);
        Requested("1396", RequestStatus.Pending, "TvShow");

        var list = await _manager.GetWatchlistAsync(movie.ProfileId);

        list.Single(i => i.Type == "TvShow").IsRequested.Should().BeTrue();
        list.Single(i => i.Type == "Movie").IsRequested.Should().BeFalse();
    }
}
