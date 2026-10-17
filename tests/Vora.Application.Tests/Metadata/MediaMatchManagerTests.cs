using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Metadata;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class MediaMatchManagerTests
{
    private static readonly Guid LibraryId = Guid.Parse("ebcec80d-1806-431a-87a9-9201ffc76daf");
    private static readonly Guid DuplicateShowId = Guid.Parse("90d7cd5d-4bb4-4fae-b787-d399b7662661");
    private static readonly Guid ExistingShowId = Guid.Parse("85091fde-56f3-4910-a4a2-759ab027aa2d");

    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaDedupeManager _dedupe = Substitute.For<IMediaDedupeManager>();
    private readonly ITaskQueueManager _taskQueue = Substitute.For<ITaskQueueManager>();
    private readonly Vora.Application.Metadata.Local.ILocalMediaReader _localMedia = Substitute.For<Vora.Application.Metadata.Local.ILocalMediaReader>();
    private readonly IMetadataProvider _tvdb = Provider("tvdb_metadata", "TVDB");
    private readonly IMetadataProvider _tmdb = Provider("tmdb_metadata", "TMDB");

    private static IMetadataProvider Provider(string id, string name)
    {
        var provider = Substitute.For<IMetadataProvider>();
        provider.Id.Returns(id);
        provider.ProviderName.Returns(name);
        return provider;
    }

    private MediaMatchManager Manager() =>
        new(_repository, [_tvdb, _tmdb], _dedupe, _taskQueue, _localMedia, Substitute.For<Vora.Application.Artwork.IMediaArtworkRepository>(), NullLogger<MediaMatchManager>.Instance);

    private static TvShow DuplicateShow(string providerId = "tvdb_metadata") => new()
    {
        Id = DuplicateShowId,
        Title = "The Walking Dead - Dead City [imdb-]",
        LibraryId = LibraryId,
        Library = new MediaLibrary { Name = "Shows", MetadataProviderId = providerId },
        TmdbId = "999",
        ImdbId = "tt0000001",
        TvdbId = "111"
    };

    private void Holds(MediaItem item) => _repository.GetForMetadataSyncAsync(item.Id).Returns(item);

    [Fact]
    public async Task Search_uses_the_cleaned_title_when_no_query_is_given()
    {
        Holds(DuplicateShow());
        _tvdb.SearchTvShowCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new MetadataSearchCandidate { Source = "tvdb", ExternalId = "417549", Title = "The Walking Dead: Dead City", Year = 2023 }]);

        var results = await Manager().SearchAsync(DuplicateShowId, null, null, TestContext.Current.CancellationToken);

        await _tvdb.Received(1).SearchTvShowCandidatesAsync("The Walking Dead - Dead City", null, Arg.Any<CancellationToken>());
        results.Should().ContainSingle().Which.Should().BeEquivalentTo(new MediaMatchCandidateVM
        {
            Source = "tvdb",
            ExternalId = "417549",
            Title = "The Walking Dead: Dead City",
            Year = 2023,
            ProviderName = "TVDB"
        });
    }

    [Fact]
    public async Task Search_uses_the_library_provider_for_a_show()
    {
        Holds(DuplicateShow("tvdb_metadata"));

        await Manager().SearchAsync(DuplicateShowId, "Dead City", null, TestContext.Current.CancellationToken);

        await _tvdb.Received(1).SearchTvShowCandidatesAsync("Dead City", null, Arg.Any<CancellationToken>());
        await _tmdb.DidNotReceive().SearchTvShowCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_falls_back_to_tmdb_for_a_library_on_local_metadata()
    {
        Holds(DuplicateShow("local_metadata"));

        await Manager().SearchAsync(DuplicateShowId, "Dead City", null, TestContext.Current.CancellationToken);

        await _tmdb.Received(1).SearchTvShowCandidatesAsync("Dead City", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_uses_movie_search_for_a_movie()
    {
        Holds(new Movie { Id = DuplicateShowId, Title = "Arrival (2016)", LibraryId = LibraryId, Library = new MediaLibrary { Name = "Movies" } });

        await Manager().SearchAsync(DuplicateShowId, null, 2016, TestContext.Current.CancellationToken);

        await _tmdb.Received(1).SearchMovieCandidatesAsync("Arrival", 2016, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_retries_without_the_year_when_the_year_finds_nothing()
    {
        Holds(DuplicateShow());

        await Manager().SearchAsync(DuplicateShowId, "Dead City", 2024, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _tvdb.SearchTvShowCandidatesAsync("Dead City", 2024, Arg.Any<CancellationToken>());
            _tvdb.SearchTvShowCandidatesAsync("Dead City", null, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_pasted_imdb_id_is_looked_up_rather_than_searched()
    {
        Holds(DuplicateShow());
        _tvdb.FetchTvShowMetadataByIdAsync("tt18546730", "imdb", Arg.Any<CancellationToken>())
            .Returns(new MetadataResult { Title = "The Walking Dead: Dead City", ReleaseDate = new DateOnly(2023, 6, 18) });

        var results = await Manager().SearchAsync(DuplicateShowId, "https://www.imdb.com/title/tt18546730/", null, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Source = "imdb",
            ExternalId = "tt18546730",
            Title = "The Walking Dead: Dead City",
            Year = 2023
        });
        await _tvdb.DidNotReceive().SearchTvShowCandidatesAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_pasted_tmdb_movie_link_finds_nothing_for_a_show()
    {
        Holds(DuplicateShow());

        var results = await Manager().SearchAsync(DuplicateShowId, "https://www.themoviedb.org/movie/603-the-matrix", null, TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_movies_and_shows_can_be_matched()
    {
        Holds(new Episode { Id = DuplicateShowId, Title = "Tenebrae", LibraryId = LibraryId });

        var act = () => Manager().SearchAsync(DuplicateShowId, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        var act = () => Manager().SearchAsync(Guid.NewGuid(), null, null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Applying_replaces_every_stale_id_with_the_chosen_one()
    {
        var show = DuplicateShow();
        Holds(show);

        await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "tvdb", ExternalId = "417549" }, TestContext.Current.CancellationToken);

        show.TvdbId.Should().Be("417549");
        show.TmdbId.Should().BeNull();
        show.ImdbId.Should().BeNull();
        await _repository.Received(1).UpdateMediaItemAsync(show);
    }

    [Fact]
    public async Task Applying_marks_the_item_as_matched_by_hand_so_a_backup_keeps_the_choice()
    {
        var show = DuplicateShow();
        Holds(show);

        await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "tvdb", ExternalId = "417549" }, TestContext.Current.CancellationToken);

        show.LockedFields.Should().Contain(MediaMatchManager.MatchLock);
    }

    [Fact]
    public async Task Applying_queues_a_refresh_of_the_matched_item()
    {
        var show = DuplicateShow();
        Holds(show);

        var result = await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "tmdb", ExternalId = "194583" }, TestContext.Current.CancellationToken);

        _taskQueue.Received(1).QueueRefreshMatchedMediaItem(show.Id, LibraryId, true);
        result.Should().BeEquivalentTo(new MediaMatchResultVM { MediaItemId = show.Id, MergedDuplicate = false });
        await _dedupe.DidNotReceive().MergeDuplicateTvShowsAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applying_accepts_a_pasted_link_as_the_id()
    {
        var show = DuplicateShow();
        Holds(show);

        await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "imdb", ExternalId = "https://www.imdb.com/title/tt18546730/" }, TestContext.Current.CancellationToken);

        show.ImdbId.Should().Be("tt18546730");
    }

    [Fact]
    public async Task Applying_to_a_show_that_already_exists_merges_them_and_points_at_the_survivor()
    {
        var show = DuplicateShow();
        Holds(show);
        _repository.FindOtherItemByExternalIdAsync(LibraryId, show.Id, true, "tvdb", "417549")
            .Returns(new MatchedItemIds(ExistingShowId, "194583", "tt18546730", "417549"));
        _dedupe.MergeDuplicateTvShowsAsync(LibraryId, Arg.Any<CancellationToken>())
            .Returns(new TvShowMergeResultVM { GroupsMerged = 1, ShowsRemoved = 1, PartsMoved = 2 });
        _repository.MediaItemExistsAsync(show.Id).Returns(false);

        var result = await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "tvdb", ExternalId = "417549" }, TestContext.Current.CancellationToken);

        show.TmdbId.Should().Be("194583");
        show.ImdbId.Should().Be("tt18546730");
        result.Should().BeEquivalentTo(new MediaMatchResultVM { MediaItemId = ExistingShowId, MergedDuplicate = true });
        _taskQueue.Received(1).QueueRefreshMatchedMediaItem(ExistingShowId, LibraryId, true);
    }

    [Fact]
    public async Task When_the_merge_keeps_this_show_the_result_stays_on_it()
    {
        var show = DuplicateShow();
        Holds(show);
        _repository.FindOtherItemByExternalIdAsync(LibraryId, show.Id, true, "tvdb", "417549")
            .Returns(new MatchedItemIds(ExistingShowId, null, null, "417549"));
        _dedupe.MergeDuplicateTvShowsAsync(LibraryId, Arg.Any<CancellationToken>())
            .Returns(new TvShowMergeResultVM { GroupsMerged = 1, ShowsRemoved = 1 });
        _repository.MediaItemExistsAsync(show.Id).Returns(true);

        var result = await Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = "tvdb", ExternalId = "417549" }, TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new MediaMatchResultVM { MediaItemId = show.Id, MergedDuplicate = true });
    }

    [Fact]
    public async Task A_movie_is_never_merged()
    {
        var movie = new Movie { Id = DuplicateShowId, Title = "Arrival", LibraryId = LibraryId, Library = new MediaLibrary { Name = "Movies" } };
        Holds(movie);
        _repository.FindOtherItemByExternalIdAsync(LibraryId, movie.Id, false, "tmdb", "329865")
            .Returns(new MatchedItemIds(ExistingShowId, "329865", null, null));

        var result = await Manager().ApplyAsync(movie.Id, new ApplyMediaMatchRequest { Source = "tmdb", ExternalId = "329865" }, TestContext.Current.CancellationToken);

        await _dedupe.DidNotReceive().MergeDuplicateTvShowsAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        result.MediaItemId.Should().Be(movie.Id);
        _taskQueue.Received(1).QueueRefreshMatchedMediaItem(movie.Id, LibraryId, false);
    }

    [Theory]
    [InlineData("trakt", "123")]
    [InlineData("imdb", "not-an-id")]
    [InlineData("tvdb", "")]
    public async Task An_invalid_match_is_rejected_before_anything_changes(string source, string externalId)
    {
        var show = DuplicateShow();
        Holds(show);

        var act = () => Manager().ApplyAsync(show.Id, new ApplyMediaMatchRequest { Source = source, ExternalId = externalId });

        await act.Should().ThrowAsync<ArgumentException>();
        await _repository.DidNotReceive().UpdateMediaItemAsync(Arg.Any<MediaItem>());
        _taskQueue.DidNotReceive().QueueRefreshMatchedMediaItem(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_movie_shows_the_file_name_of_each_version()
    {
        var movie = new Movie { Id = DuplicateShowId, Title = "Heat", LibraryId = LibraryId, Library = new MediaLibrary { Name = "Movies" } };
        movie.MediaParts.Add(new MediaPart { FilePath = "/media/movies/Heat (1995)/Heat (1995) 2160p.mkv" });
        movie.MediaParts.Add(new MediaPart { FilePath = "/media/movies/Heat (1995)/Heat (1995) 1080p.mkv" });
        Holds(movie);

        var files = await Manager().GetFilesAsync(movie.Id);

        files.FileNames.Should().Equal("Heat (1995) 1080p.mkv", "Heat (1995) 2160p.mkv");
        files.FolderNames.Should().BeEmpty();
    }

    private static Movie MatchedByHand() => new()
    {
        Id = DuplicateShowId,
        Title = "The film chosen by hand",
        ReleaseDate = new DateOnly(1999, 3, 31),
        LibraryId = LibraryId,
        Library = new MediaLibrary { Name = "Movies" },
        TmdbId = "603",
        ImdbId = "tt0133093",
        LastMetadataRefresh = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        LockedFields = [MediaMatchManager.MatchLock]
    };

    [Fact]
    public async Task Clearing_a_manual_match_names_the_title_from_its_file_again_and_refreshes_it_without_overriding_locks()
    {
        var movie = MatchedByHand();
        Holds(movie);
        _localMedia.ReadNamingAsync(movie.Id).Returns(new Vora.Application.Metadata.Local.LocalNaming("Underworld", 2003, ExternalIdSet.Empty));

        await Manager().ClearAsync(movie.Id);

        movie.IsLocked(MediaMatchManager.MatchLock).Should().BeFalse();
        movie.TmdbId.Should().BeNull();
        movie.ImdbId.Should().BeNull();
        movie.Title.Should().Be("Underworld");
        movie.ReleaseDate.Should().Be(new DateOnly(2003, 1, 1));
        movie.LastMetadataRefresh.Should().BeNull();
        await _repository.Received(1).UpdateMediaItemAsync(movie);
        _taskQueue.Received(1).QueueRefreshMediaItemMetadata(movie.Id, "Underworld", false, LibraryId);
    }

    [Fact]
    public async Task Clearing_a_manual_match_goes_back_to_the_id_written_in_the_file_name()
    {
        var movie = MatchedByHand();
        Holds(movie);
        _localMedia.ReadNamingAsync(movie.Id).Returns(new Vora.Application.Metadata.Local.LocalNaming("Underworld", 2003, ExternalIdSet.From(tmdbId: "277")));

        await Manager().ClearAsync(movie.Id);

        movie.TmdbId.Should().Be("277");
        movie.ImdbId.Should().BeNull();
    }

    [Fact]
    public async Task Clearing_a_manual_match_keeps_a_title_and_ids_the_admin_locked()
    {
        var movie = MatchedByHand();
        movie.LockField(nameof(movie.Title));
        movie.LockField(nameof(movie.TmdbId));
        Holds(movie);
        _localMedia.ReadNamingAsync(movie.Id).Returns(new Vora.Application.Metadata.Local.LocalNaming("Underworld", 2003, ExternalIdSet.Empty));

        await Manager().ClearAsync(movie.Id);

        movie.Title.Should().Be("The film chosen by hand");
        movie.TmdbId.Should().Be("603");
        movie.IsLocked(MediaMatchManager.MatchLock).Should().BeFalse();
    }

    [Fact]
    public async Task Clearing_a_shows_manual_match_has_its_episodes_read_again_too()
    {
        var show = DuplicateShow();
        show.LockField(MediaMatchManager.MatchLock);
        Holds(show);
        var episodes = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _repository.GetEpisodeIdsForShowAsync(show.Id).Returns(episodes);

        await Manager().ClearAsync(show.Id);

        await _repository.Received(1).ClearMetadataRefreshAsync(episodes);
        _taskQueue.Received(1).QueueRefreshMediaItemMetadata(show.Id, Arg.Any<string?>(), false, LibraryId);
    }

    [Fact]
    public async Task A_title_that_was_never_matched_by_hand_is_left_exactly_as_it_is()
    {
        var movie = MatchedByHand();
        movie.UnlockField(MediaMatchManager.MatchLock);
        Holds(movie);

        await Manager().ClearAsync(movie.Id);

        movie.TmdbId.Should().Be("603");
        await _repository.DidNotReceive().UpdateMediaItemAsync(Arg.Any<MediaItem>());
        _taskQueue.DidNotReceive().QueueRefreshMediaItemMetadata(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task A_show_shows_the_folders_its_episodes_live_in()
    {
        var show = DuplicateShow();
        show.Library.FolderPaths = ["/media/tv"];
        Holds(show);
        _repository.GetEpisodeFilePathsForShowAsync(show.Id).Returns([
            "/media/tv/The Walking Dead - Dead City [imdb-]/Season 01/Dead.City.S01E01.mkv",
            "/media/tv/The Walking Dead - Dead City [imdb-]/Season 01/Dead.City.S01E02.mkv",
            "/media/tv/The Walking Dead Dead City (2023)/Season 02/Dead.City.S02E01.mkv",
        ]);

        var files = await Manager().GetFilesAsync(show.Id);

        files.FolderNames.Should().Equal("The Walking Dead - Dead City [imdb-]", "The Walking Dead Dead City (2023)");
        files.FileNames.Should().BeEmpty();
    }
}
