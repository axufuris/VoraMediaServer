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
        new(_repository, [_tvdb, _tmdb], _dedupe, _taskQueue, NullLogger<MediaMatchManager>.Instance);

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
}
