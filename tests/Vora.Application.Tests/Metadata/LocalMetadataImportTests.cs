using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;
using LocalMetadataMode = Vora.Domain.Enums.LocalMetadataMode;

namespace Vora.Application.Tests.Metadata;

public class LocalMetadataImportTests : IDisposable
{
    private const string OnlineProvider = "tmdb_metadata";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-nfo-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMetadataProvider _remote = Substitute.For<IMetadataProvider>();
    private readonly IArtworkProvider _remoteArtwork = Substitute.For<IArtworkProvider>();
    private readonly RecordingLogger _log = new();

    public LocalMetadataImportTests()
    {
        Directory.CreateDirectory(_directory);
        _remote.Id.Returns(OnlineProvider);
        _remote.ProviderName.Returns("TMDB");
        _remoteArtwork.Id.Returns("tmdb_artwork");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingLogger : ILogger<LocalMediaReader>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private string Write(string name, string contents = "")
    {
        var path = Path.GetFullPath(Path.Combine(_directory, name));
        var parent = Path.GetDirectoryName(path);
        if (parent != null) Directory.CreateDirectory(parent);
        File.WriteAllText(path, contents);
        return path;
    }

    private void Fixture(string name, string target) => File.Copy(
        Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", name), Path.Combine(_directory, target));

    private static MediaLibrary Library(LocalMetadataMode mode, bool preferLocalArtwork = false, string name = "Movies") => new()
    {
        Name = name,
        MetadataProviderId = OnlineProvider,
        ArtworkProviderId = "tmdb_artwork",
        LocalMetadataMode = mode,
        PreferLocalArtwork = preferLocalArtwork
    };

    private Movie Movie(string name = "Underworld (2003).mkv", LocalMetadataMode mode = LocalMetadataMode.Prefer, bool preferLocalArtwork = false)
    {
        var movie = new Movie
        {
            Title = "Filename title",
            Library = Library(mode, preferLocalArtwork),
            MediaParts = [new MediaPart { FilePath = Write(name) }]
        };
        Known(movie, LocalMediaSourceDto.Movie, [movie.MediaParts.Single().FilePath]);
        return movie;
    }

    private void Known(MediaItem item, string kind, IReadOnlyList<string> paths, int? season = null, int? episode = null) =>
        _repository.GetLocalMediaSourceAsync(item.Id).Returns(new LocalMediaSourceDto(item.Id, kind, season, episode, paths));

    private MetadataFetchService Fetch() => new(
        [_remote, new LocalMetadataProvider(NullLogger<LocalMetadataProvider>.Instance)],
        [],
        [_remoteArtwork, new LocalArtworkProvider(NullLogger<LocalArtworkProvider>.Instance)],
        new LocalMediaReader(_repository, _log),
        Substitute.For<IVideoFrameArtworkService>());

    private void OnlineAnswers(MetadataResult? result)
    {
        _remote.FetchMovieMetadataAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(result);
        _remote.FetchMovieMetadataByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);
        _remote.FetchTvShowMetadataAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(result);
        _remote.FetchTvShowMetadataByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);
        _remote.FetchEpisodeMetadataAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(result);
    }

    private void OnlineArtworkAnswers(params ArtworkResult[] results) =>
        _remoteArtwork.GetArtworkAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(results);

    [Fact]
    public async Task A_library_that_does_not_read_nfo_files_never_opens_one()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var online = new MetadataResult { Title = "Online title" };
        OnlineAnswers(online);
        var movie = Movie(mode: LocalMetadataMode.Off);

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.Metadata.Should().BeSameAs(online);
        result.ProviderId.Should().Be(OnlineProvider);
        movie.TmdbId.Should().BeNull();
        await _repository.DidNotReceive().GetLocalMediaSourceAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Matching_only_takes_the_identity_from_the_nfo_and_every_detail_from_the_provider()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var online = new MetadataResult { Title = "Online title", Overview = "Online plot" };
        _remote.FetchMovieMetadataByIdAsync("277", "tmdb", Arg.Any<CancellationToken>()).Returns(online);
        var movie = Movie(mode: LocalMetadataMode.MatchOnly);

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.Metadata.Should().BeSameAs(online);
        result.ProviderId.Should().Be(OnlineProvider);
        movie.TmdbId.Should().Be("277");
        movie.ImdbId.Should().Be("tt0320691");
        movie.TvdbId.Should().Be("1154");
    }

    [Theory]
    [InlineData("EmbyMovie.nfo", "movie.nfo")]
    [InlineData("EmbySidecar.nfo", "Underworld (2003).nfo")]
    public async Task Preferring_the_nfo_lays_its_details_over_the_providers_and_keeps_what_only_the_provider_knows(string fixture, string target)
    {
        Fixture(fixture, target);
        OnlineAnswers(new MetadataResult
        {
            Title = "Online title",
            Overview = "Online plot",
            Status = "Released",
            Budget = 22_000_000,
            HomePage = "https://example.com/underworld",
            TmdbId = "277",
            Cast =
            [
                new CastMemberResult { Name = "Kate Beckinsale", TmdbId = 3967, ProfileImageUrl = "https://image.tmdb.org/t/p/w185/kate.jpg" },
                new CastMemberResult { Name = "Someone the NFO does not list", TmdbId = 1 }
            ],
            ProductionCompanies = [new CompanyResult { Id = 3, Name = "Lakeshore" }],
            Videos = [new VideoResult { Key = "online00001", Name = "Featurette", Site = "YouTube", Type = "Featurette" }]
        });

        var result = await Fetch().GetTextMetadataAsync(Movie());

        var metadata = Assert.IsType<MetadataResult>(result.Metadata);
        result.ProviderId.Should().Be(OnlineProvider);
        metadata.Title.Should().Be("Underworld");
        metadata.Overview.Should().Contain("Selene");
        metadata.ReleaseDate.Should().Be(new DateOnly(2003, 9, 19));
        metadata.ContentRating.Should().Be("R");
        metadata.GenreIds.Should().BeEquivalentTo([14, 28, 53]);
        metadata.Status.Should().Be("Released");
        metadata.Budget.Should().Be(22_000_000);
        metadata.HomePage.Should().Be("https://example.com/underworld");
        metadata.ProductionCompanies.Should().ContainSingle(company => company.Name == "Lakeshore");
        metadata.Cast.Should().Contain(actor => actor.Name == "Kate Beckinsale" && actor.CharacterName == "Selene"
            && actor.TmdbId == 3967 && actor.ProfileImageUrl == "https://image.tmdb.org/t/p/w185/kate.jpg");
        metadata.Cast.Should().Contain(actor => actor.Name == "Len Wiseman" && actor.Roles == CastRole.Director);
        metadata.Cast.Should().NotContain(actor => actor.Name == "Someone the NFO does not list");
        metadata.Cast.Should().NotContain(actor => actor.ProfileImageUrl != null && actor.ProfileImageUrl.StartsWith("/config/"));
        metadata.Videos.Select(video => video.Key).Should().Equal("mn4O3iQ8B_s", "2_IoL7g5Ub8", "online00001");
    }

    [Fact]
    public async Task Preferring_the_nfo_uses_it_alone_when_the_provider_has_no_answer()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        OnlineAnswers(null);

        var result = await Fetch().GetTextMetadataAsync(Movie());

        var metadata = Assert.IsType<MetadataResult>(result.Metadata);
        result.ProviderId.Should().Be(LocalMetadataProvider.ProviderId);
        metadata.Title.Should().Be("Underworld");
        metadata.OriginalTitle.Should().Be("Underworld");
        metadata.TmdbId.Should().Be("277");
        metadata.ImdbId.Should().Be("tt0320691");
        metadata.TvdbId.Should().Be("1154");
    }

    [Fact]
    public async Task The_provider_is_asked_by_the_nfo_title_when_neither_the_nfo_nor_the_item_has_an_id()
    {
        Write("Underworld (2003).nfo", "<movie><title>Underworld</title><year>2003</year></movie>");
        OnlineAnswers(null);

        await Fetch().GetTextMetadataAsync(Movie(mode: LocalMetadataMode.MatchOnly));

        await _remote.Received(1).FetchMovieMetadataAsync("Underworld", 2003, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Each_film_in_a_shared_folder_reads_only_its_own_nfo()
    {
        Write("movie.nfo", "<movie><title>Folder default</title><tmdbid>1</tmdbid></movie>");
        Write("First (2001).nfo", "<movie><title>First title</title></movie>");
        Write("Second (2002).NFO", "<movie><title>Second title</title></movie>");
        OnlineAnswers(null);
        var first = Movie("First (2001).mkv");
        var second = Movie("Second (2002).mkv");
        var third = Movie("Third (2003).mkv");

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(first)).Metadata).Title.Should().Be("First title");
        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(second)).Metadata).Title.Should().Be("Second title");
        (await Fetch().GetTextMetadataAsync(third)).Metadata.Should().BeNull();
        third.TmdbId.Should().BeNull();
    }

    [Fact]
    public async Task Two_versions_of_one_film_share_the_folders_movie_nfo()
    {
        Write("movie.nfo", "<movie><title>Underworld</title><tmdbid>277</tmdbid></movie>");
        Write("Underworld (2003) - 4K.mkv");
        OnlineAnswers(null);

        var result = await Fetch().GetTextMetadataAsync(Movie("Underworld (2003) - 1080p.mkv"));

        Assert.IsType<MetadataResult>(result.Metadata).Title.Should().Be("Underworld");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<movie><title>unfinished")]
    [InlineData("<tvshow><title>Wrong type</title></tvshow>")]
    [InlineData("<movie />")]
    [InlineData("<!DOCTYPE movie [<!ENTITY external SYSTEM 'file:///secret'>]><movie><title>&external;</title></movie>")]
    [InlineData("Release notes from the group that packed this file.\r\nNot XML at all.")]
    public async Task An_nfo_that_cannot_be_used_leaves_the_provider_in_charge(string contents)
    {
        Write("movie.nfo", contents);
        var expected = new MetadataResult { Title = "Online title" };
        OnlineAnswers(expected);

        var result = await Fetch().GetTextMetadataAsync(Movie());

        result.Metadata.Should().BeSameAs(expected);
        result.ProviderId.Should().Be(OnlineProvider);
    }

    [Fact]
    public async Task A_release_groups_text_nfo_is_skipped_without_a_warning()
    {
        Write("Underworld (2003).nfo", "Release notes from the group that packed this file.");
        OnlineAnswers(null);

        await Fetch().GetTextMetadataAsync(Movie());

        _log.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_malformed_sidecar_falls_back_to_the_folders_movie_nfo()
    {
        Write("Underworld (2003).nfo", "<movie><title>broken");
        Write("movie.nfo", "<movie><title>Underworld</title></movie>");
        OnlineAnswers(null);

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie())).Metadata).Title.Should().Be("Underworld");
    }

    [Fact]
    public async Task An_nfo_for_a_film_from_another_year_is_not_trusted()
    {
        Write("Underworld (2003).nfo", "<movie><title>Some other film</title><year>1985</year><tmdbid>9</tmdbid></movie>");
        var online = new MetadataResult { Title = "Online title" };
        OnlineAnswers(online);
        var movie = Movie();

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.Metadata.Should().BeSameAs(online);
        movie.TmdbId.Should().BeNull();
    }

    [Fact]
    public async Task An_id_in_the_file_name_beats_a_different_id_in_the_nfo()
    {
        Write("Underworld (2003) {tmdb-277}.nfo", "<movie><title>Some other film</title><tmdbid>9</tmdbid></movie>");
        var online = new MetadataResult { Title = "Online title" };
        OnlineAnswers(online);
        var movie = Movie("Underworld (2003) {tmdb-277}.mkv");
        movie.TmdbId = "277";

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.Metadata.Should().BeSameAs(online);
        movie.TmdbId.Should().Be("277");
    }

    [Fact]
    public async Task A_match_made_by_hand_is_not_undone_by_an_nfo_that_names_another_title()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var online = new MetadataResult { Title = "The film chosen by hand", TmdbId = "555" };
        OnlineAnswers(online);
        var movie = Movie();
        movie.TmdbId = "555";
        movie.LockField(MediaMatchManager.MatchLock);

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.Metadata.Should().BeSameAs(online);
        movie.TmdbId.Should().Be("555");
        movie.ImdbId.Should().BeNull();
        await _remote.Received(1).FetchMovieMetadataByIdAsync("555", "tmdb", Arg.Any<CancellationToken>());
        await _remote.DidNotReceive().FetchMovieMetadataByIdAsync("277", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_match_made_by_hand_still_takes_details_from_an_nfo_for_the_same_title()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        OnlineAnswers(new MetadataResult { Title = "Online title", TmdbId = "277" });
        var movie = Movie();
        movie.TmdbId = "277";
        movie.LockField(MediaMatchManager.MatchLock);

        var result = await Fetch().GetTextMetadataAsync(movie);

        Assert.IsType<MetadataResult>(result.Metadata).Title.Should().Be("Underworld");
        movie.TmdbId.Should().Be("277");
    }

    [Fact]
    public async Task An_nfo_with_a_different_id_replaces_ids_that_came_from_an_automatic_match()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        OnlineAnswers(null);
        var movie = Movie(mode: LocalMetadataMode.MatchOnly);
        movie.TmdbId = "999";
        movie.TvdbId = "888";

        await Fetch().GetTextMetadataAsync(movie);

        movie.TmdbId.Should().Be("277");
        movie.ImdbId.Should().Be("tt0320691");
        movie.TvdbId.Should().Be("1154");
    }

    [Fact]
    public async Task The_local_provider_on_its_own_still_reads_the_nfo_whatever_the_library_switch_says()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var movie = Movie(mode: LocalMetadataMode.Off);
        movie.Library.MetadataProviderId = LocalMetadataProvider.ProviderId;

        var result = await Fetch().GetTextMetadataAsync(movie);

        result.ProviderId.Should().Be(LocalMetadataProvider.ProviderId);
        Assert.IsType<MetadataResult>(result.Metadata).Title.Should().Be("Underworld");
        await _remote.DidNotReceive().FetchMovieMetadataByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_unique_ids_year_sort_title_and_decimal_rating_independent_of_culture()
    {
        Write("movie.nfo", """
            <movie><title>Title</title><sorttitle>Sort key</sorttitle><year>2003</year><rating>7.5</rating><mpaa>Rated PG-13</mpaa>
            <uniqueid type="tmdb">277</uniqueid><uniqueid type="imdb">tt0320691</uniqueid><uniqueid type="tvdb">1154</uniqueid>
            </movie>
            """);
        OnlineAnswers(null);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var metadata = Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie())).Metadata);
            metadata.ReleaseDate.Should().Be(new DateOnly(2003, 1, 1));
            metadata.SortTitle.Should().Be("Sort key");
            metadata.Rating.Should().Be(7.5m);
            metadata.ContentRating.Should().Be("PG-13");
            metadata.TmdbId.Should().Be("277");
            metadata.ImdbId.Should().Be("tt0320691");
            metadata.TvdbId.Should().Be("1154");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task An_old_style_imdb_id_element_is_read_as_the_imdb_id()
    {
        Write("movie.nfo", "<movie><title>Underworld</title><id>tt0320691</id></movie>");
        OnlineAnswers(null);

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie())).Metadata).ImdbId.Should().Be("tt0320691");
    }

    [Fact]
    public async Task Local_images_are_left_alone_unless_the_library_prefers_them()
    {
        Write("poster.jpg");
        OnlineArtworkAnswers(new ArtworkResult { Kind = Vora.Plugins.Dtos.ArtworkKind.Poster, Url = "https://image.tmdb.org/t/p/w500/online.jpg", VoteAverage = 8 });

        var artwork = await Fetch().GetArtworkAsync(Movie(preferLocalArtwork: false));

        artwork.Should().ContainSingle().Which.ProviderId.Should().Be("tmdb_artwork");
    }

    [Fact]
    public async Task Preferred_local_images_are_listed_beside_the_online_ones_by_an_address_not_a_path()
    {
        Write("poster.JPG");
        Write("backdrop.jpg");
        Write("logo.png");
        OnlineArtworkAnswers(
            new ArtworkResult { Kind = Vora.Plugins.Dtos.ArtworkKind.Poster, Url = "https://image.tmdb.org/t/p/w500/online.jpg", VoteAverage = 10 },
            new ArtworkResult { Kind = Vora.Plugins.Dtos.ArtworkKind.Backdrop, Url = "https://image.tmdb.org/t/p/w1280/online.jpg" });
        var movie = Movie(preferLocalArtwork: true);

        var artwork = await Fetch().GetArtworkAsync(movie);

        var local = artwork.Where(a => a.ProviderId == LocalArtworkProvider.ProviderId).ToList();
        local.Select(a => a.Kind).Should().BeEquivalentTo([ArtworkKind.Poster, ArtworkKind.Backdrop, ArtworkKind.Logo]);
        local.Should().OnlyContain(a => a.Url.StartsWith($"/api/artwork/local/{movie.Id}/") && !a.Url.Contains(_directory));
        local.Single(a => a.Kind == ArtworkKind.Poster).Url.Should().Contain("/poster.JPG?v=");
        artwork.Where(a => a.ProviderId == "tmdb_artwork").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_film_takes_images_named_after_its_own_file_and_never_a_neighbours()
    {
        Write("poster.nfo", "<movie><title>Not an image</title></movie>");
        Write("poster.jpg");
        Write("First (2001)-poster.png");
        Write("First (2001)-logo.png");
        Write("Second (2002).mkv");
        OnlineArtworkAnswers();

        var artwork = await Fetch().GetArtworkAsync(Movie("First (2001).mkv", preferLocalArtwork: true));

        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Poster).Which.Url.Should().Contain("First%20%282001%29-poster.png");
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Logo).Which.Url.Should().Contain("First%20%282001%29-logo.png");
        artwork.Should().NotContain(a => a.Url.Contains("/poster.jpg") || a.Url.Contains(".nfo"));
    }

    private (TvShow Show, Season Season, Episode Episode) Show(string folder, string episodeFile, LocalMetadataMode mode = LocalMetadataMode.Prefer, bool preferLocalArtwork = true, int seasonNumber = 1)
    {
        var library = Library(mode, preferLocalArtwork, "TV");
        var episodePath = Write(Path.Combine(folder, episodeFile));
        var show = new TvShow { Title = "Filename show", Library = library };
        var season = new Season { Title = $"Season {seasonNumber}", SeasonNumber = seasonNumber, TvShow = show, Library = library };
        var episode = new Episode { Title = "Filename title", EpisodeNumber = 1, Season = season, Library = library, MediaParts = [new MediaPart { FilePath = episodePath }] };
        Known(show, LocalMediaSourceDto.TvShow, [episodePath]);
        Known(season, LocalMediaSourceDto.Season, [episodePath], seasonNumber);
        Known(episode, LocalMediaSourceDto.Episode, [episodePath], seasonNumber, 1);
        return (show, season, episode);
    }

    [Fact]
    public async Task A_show_and_its_season_read_their_nfo_and_images_from_where_the_episodes_are()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Imported show</title><tvdbid>123</tvdbid></tvshow>");
        Write("Show/Season 01/season.nfo", "<season><title>Imported season</title><plot>Season plot</plot></season>");
        Write("Show/poster.jpg");
        Write("Show/Season 01/poster.jpg");
        OnlineAnswers(null);
        OnlineArtworkAnswers();
        var (show, season, _) = Show("Show/Season 01", "Show.S01E01.mkv");
        var fetch = Fetch();

        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(show)).Metadata).Title.Should().Be("Imported show");
        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(season)).Metadata).Title.Should().Be("Imported season");
        (await fetch.GetArtworkAsync(show)).Should().ContainSingle(a => a.Kind == ArtworkKind.Poster).Which.Url.Should().StartWith($"/api/artwork/local/{show.Id}/poster.jpg");
        (await fetch.GetArtworkAsync(season)).Should().ContainSingle(a => a.Kind == ArtworkKind.Poster).Which.Url.Should().StartWith($"/api/artwork/local/{season.Id}/poster.jpg");
    }

    [Theory]
    [InlineData("Special Ops Lioness (2023)")]
    [InlineData("Season of the Witch")]
    [InlineData("Seasons")]
    public async Task A_show_kept_in_one_flat_folder_reads_its_images_from_that_folder_not_the_library_root(string folder)
    {
        Write("poster.jpg");
        Write(Path.Combine(folder, "poster.jpg"));
        Write(Path.Combine(folder, "fanart.jpg"));
        OnlineArtworkAnswers();
        var (show, _, _) = Show(folder, "Show.S01E01.mkv");

        var source = Assert.IsType<LocalMediaSourceDto>(await _repository.GetLocalMediaSourceAsync(show.Id));
        var place = LocalMediaPlace.From(source, new LocalMediaFolders(NullLogger.Instance));
        var artwork = await Fetch().GetArtworkAsync(show);

        Assert.IsType<LocalMediaPlace>(place).Paths.Should().Equal(Path.Combine(_directory, folder));
        artwork.Select(a => a.Kind).Should().BeEquivalentTo([ArtworkKind.Poster, ArtworkKind.Backdrop]);
    }

    [Fact]
    public async Task A_folder_that_starts_with_Season_but_holds_tvshow_nfo_is_the_show_folder()
    {
        Write("Season One/tvshow.nfo", "<tvshow><title>Season One</title></tvshow>");
        OnlineAnswers(null);
        var (show, _, _) = Show("Season One", "Season.One.S01E01.mkv");

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(show)).Metadata).Title.Should().Be("Season One");
    }

    [Fact]
    public async Task A_flat_shows_poster_is_not_handed_to_a_season_that_has_none_of_its_own()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Show</title></tvshow>");
        Write("Show/poster.jpg");
        OnlineArtworkAnswers();
        var (_, season, _) = Show("Show", "Show.S01E01.mkv");

        (await Fetch().GetArtworkAsync(season)).Should().NotContain(a => a.Kind == ArtworkKind.Poster);
    }

    [Fact]
    public async Task A_numbered_season_poster_in_the_show_folder_belongs_to_that_season()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Show</title></tvshow>");
        Write("Show/season02-poster.jpg");
        Write("Show/season02-fanart.jpg");
        OnlineArtworkAnswers();
        var (_, season, _) = Show("Show/Season 02", "Show.S02E01.mkv", seasonNumber: 2);

        var artwork = await Fetch().GetArtworkAsync(season);

        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Poster).Which.Url.Should().Contain("/season02-poster.jpg");
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Backdrop).Which.Url.Should().Contain("/season02-fanart.jpg");
    }

    [Fact]
    public async Task An_episode_takes_only_its_own_sidecar_and_thumbnail()
    {
        Write("Show/Season 01/Show.S01E01.nfo", "<episodedetails><title>Pilot</title><aired>2026-01-02</aired></episodedetails>");
        Write("Show/Season 01/Show.S01E010.nfo", "<episodedetails><title>Wrong episode</title></episodedetails>");
        Write("Show/Season 01/poster.jpg");
        Write("Show/Season 01/Show.S01E01-thumb.jpg");
        OnlineAnswers(null);
        OnlineArtworkAnswers();
        var (_, _, episode) = Show("Show/Season 01", "Show.S01E01.mkv");
        var fetch = Fetch();

        var metadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episode)).Metadata);

        metadata.Title.Should().Be("Pilot");
        metadata.ReleaseDate.Should().Be(new DateOnly(2026, 1, 2));
        (await fetch.GetArtworkAsync(episode)).Should().ContainSingle().Which.Url.Should().Contain("/Show.S01E01-thumb.jpg");
    }

    [Fact]
    public async Task A_file_holding_two_episodes_gives_each_its_own_entry_from_the_shared_nfo()
    {
        Write("Show/Season 01/Show.S01E01E02.nfo", """
            <episodedetails><title>Part one</title><season>1</season><episode>1</episode></episodedetails>
            <episodedetails><title>Part two</title><season>1</season><episode>2</episode></episodedetails>
            """);
        OnlineAnswers(null);
        var (_, season, first) = Show("Show/Season 01", "Show.S01E01E02.mkv");
        var second = new Episode { Title = "Filename title", EpisodeNumber = 2, Season = season, Library = first.Library, MediaParts = [new MediaPart { FilePath = first.MediaParts.Single().FilePath }] };
        Known(second, LocalMediaSourceDto.Episode, [first.MediaParts.Single().FilePath], 1, 2);
        var fetch = Fetch();

        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(first)).Metadata).Title.Should().Be("Part one");
        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(second)).Metadata).Title.Should().Be("Part two");
        _log.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Matching_only_never_reads_episode_or_season_nfo_files()
    {
        Write("Show/Season 01/Show.S01E01.nfo", "<episodedetails><title>Pilot</title></episodedetails>");
        var online = new MetadataResult { Title = "Online episode" };
        OnlineAnswers(online);
        var (show, _, episode) = Show("Show/Season 01", "Show.S01E01.mkv", LocalMetadataMode.MatchOnly);
        show.TmdbId = "42";

        var result = await Fetch().GetTextMetadataAsync(episode);

        result.Metadata.Should().BeSameAs(online);
        await _repository.DidNotReceive().GetLocalMediaSourceAsync(episode.Id);
    }

    [Fact]
    public async Task Episodes_of_a_show_matched_by_hand_ignore_nfo_files_written_for_a_different_show()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>The wrong show</title><tmdbid>9</tmdbid></tvshow>");
        Write("Show/Season 01/Show.S01E01.nfo", "<episodedetails><title>An episode of the wrong show</title></episodedetails>");
        var online = new MetadataResult { Title = "Online episode" };
        OnlineAnswers(online);
        var (show, _, episode) = Show("Show/Season 01", "Show.S01E01.mkv");
        show.TmdbId = "42";
        show.LockField(MediaMatchManager.MatchLock);

        var result = await Fetch().GetTextMetadataAsync(episode);

        result.Metadata.Should().BeSameAs(online);
    }

    [Fact]
    public async Task The_FROM_sample_reads_the_show_and_all_ten_episodes()
    {
        const string folder = "From (2022)";
        var sampleDirectory = Path.Combine(_directory, folder);
        Directory.CreateDirectory(sampleDirectory);
        var source = Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", "From");
        foreach (var nfo in Directory.GetFiles(source, "*.nfo"))
        {
            File.Copy(nfo, Path.Combine(sampleDirectory, Path.GetFileName(nfo)));
        }
        OnlineAnswers(null);
        var library = Library(LocalMetadataMode.Prefer, name: "TV");
        var show = new TvShow { Title = "Filename show", Library = library };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShow = show, Library = library };
        var episodes = Enumerable.Range(1, 10).Select(number => new Episode
        {
            Title = "Filename title",
            EpisodeNumber = number,
            Season = season,
            Library = library,
            MediaParts = [new MediaPart { FilePath = Write(Path.Combine(folder, $"From - S01E{number:D2}.mkv")) }]
        }).ToList();
        Known(show, LocalMediaSourceDto.TvShow, episodes.Select(episode => episode.MediaParts.Single().FilePath).ToList());
        foreach (var episode in episodes)
        {
            Known(episode, LocalMediaSourceDto.Episode, [episode.MediaParts.Single().FilePath], 1, episode.EpisodeNumber);
        }
        var fetch = Fetch();

        var showMetadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(show)).Metadata);
        showMetadata.Title.Should().Be("FROM");
        showMetadata.Overview.Should().Contain("nightmarish town");
        showMetadata.TmdbId.Should().Be("124364");
        showMetadata.ImdbId.Should().Be("tt9813792");
        showMetadata.TvdbId.Should().Be("401003");
        showMetadata.ReleaseDate.Should().Be(new DateOnly(2022, 2, 20));
        showMetadata.ContentRating.Should().Be("TV-MA");
        showMetadata.Cast.Should().Contain(actor => actor.Name == "Harold Perrineau" && actor.CharacterName == "Boyd Stevens"
            && actor.TmdbId == 6195 && actor.TvdbId == 7933614);

        var expectedTitles = new[]
        {
            "Long Day's Journey Into Night", "The Way Things Are Now", "Choosing Day", "A Rock and a Farway",
            "Silhouettes", "Book 74", "All Good Things", "Broken Windows, Open Doors", "Into the Woods", "Oh, the Places We'll Go"
        };
        for (var index = 0; index < episodes.Count; index++)
        {
            var metadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episodes[index])).Metadata);
            metadata.Title.Should().Be(expectedTitles[index]);
            metadata.Overview.Should().NotBeNullOrWhiteSpace();
            metadata.ReleaseDate.Should().NotBeNull();
        }

        var pilot = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episodes[0])).Metadata);
        pilot.ReleaseDate.Should().Be(new DateOnly(2022, 2, 20));
        pilot.RuntimeMinutes.Should().Be(52);
        pilot.Cast.Should().Contain(person => person.Name == "Jack Bender" && person.Roles == CastRole.Director
            && person.TmdbId == 28976 && person.TvdbId == 248008);
    }
}
