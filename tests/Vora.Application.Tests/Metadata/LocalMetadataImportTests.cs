using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Application.Tests.Metadata;

public class LocalMetadataImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-nfo-" + Guid.NewGuid().ToString("N"));
    private readonly LocalMetadataProvider _local = new(NullLogger<LocalMetadataProvider>.Instance);
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMetadataProvider _remote = Substitute.For<IMetadataProvider>();

    public LocalMetadataImportTests()
    {
        Directory.CreateDirectory(_directory);
        _remote.Id.Returns("tmdb_metadata");
        _remote.ProviderName.Returns("TMDB");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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

    private Movie Movie(string name = "Underworld (2003).mkv") => new()
    {
        Title = "Filename title",
        Library = new MediaLibrary { Name = "Movies", MetadataProviderId = "tmdb_metadata", ArtworkProviderId = "tmdb_artwork" },
        MediaParts = [new MediaPart { FilePath = Write(name) }]
    };

    private MetadataFetchService Fetch(params IArtworkProvider[] artwork) =>
        new([_remote, _local], [], artwork, _repository);

    [Theory]
    [InlineData("EmbyMovie.nfo", "movie.nfo")]
    [InlineData("EmbySidecar.nfo", "Underworld (2003).nfo")]
    public async Task Imports_issue_69_samples_with_the_default_online_provider(string fixture, string target)
    {
        Fixture(fixture, target);
        var result = await Fetch().GetTextMetadataAsync(Movie());

        var metadata = Assert.IsType<MetadataResult>(result.Metadata);
        result.ProviderId.Should().Be("local_metadata");
        metadata.Title.Should().Be("Underworld");
        metadata.OriginalTitle.Should().Be("Underworld");
        metadata.Overview.Should().Contain("Selene");
        metadata.ReleaseDate.Should().Be(new DateOnly(2003, 9, 19));
        metadata.ContentRating.Should().Be("R");
        metadata.TmdbId.Should().Be("277");
        metadata.ImdbId.Should().Be("tt0320691");
        metadata.TvdbId.Should().Be("1154");
        metadata.GenreIds.Should().BeEquivalentTo([14, 28, 53]);
        metadata.Cast.Should().Contain(actor => actor.Name == "Kate Beckinsale" && actor.CharacterName == "Selene");
        metadata.Cast.Should().Contain(actor => actor.Name == "Len Wiseman" && actor.Roles == CastRole.Director);
        metadata.Cast.Should().NotContain(actor => actor.ProfileImageUrl != null && actor.ProfileImageUrl.StartsWith("/config/"));
        metadata.Videos.Should().Contain(video => video.Site == "YouTube" && video.Key == "mn4O3iQ8B_s");
        await _remote.DidNotReceive().FetchMovieMetadataAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Matches_each_movies_sidecar_in_a_shared_folder()
    {
        Write("movie.nfo", "<movie><title>Folder default</title></movie>");
        Write("First.nfo", "<movie><title>First title</title></movie>");
        Write("Second.NFO", "<movie><title>Second title</title></movie>");

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie("First.mkv"))).Metadata).Title.Should().Be("First title");
        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie("Second.mkv"))).Metadata).Title.Should().Be("Second title");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<movie><title>unfinished")]
    [InlineData("<tvshow><title>Wrong type</title></tvshow>")]
    [InlineData("<movie />")]
    [InlineData("<!DOCTYPE movie [<!ENTITY external SYSTEM 'file:///secret'>]><movie><title>&external;</title></movie>")]
    public async Task Unusable_nfo_falls_back_to_the_configured_provider(string contents)
    {
        Write("movie.nfo", contents);
        var expected = new MetadataResult { Title = "Online title" };
        _remote.FetchMovieMetadataAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await Fetch().GetTextMetadataAsync(Movie());

        result.Metadata.Should().BeSameAs(expected);
        result.ProviderId.Should().Be("tmdb_metadata");
    }

    [Fact]
    public async Task Malformed_sidecar_falls_back_to_movie_nfo()
    {
        Write("Underworld (2003).nfo", "broken");
        Write("movie.nfo", "<movie><title>Underworld</title></movie>");

        Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie())).Metadata).Title.Should().Be("Underworld");
    }

    [Fact]
    public async Task Artwork_alone_does_not_suppress_online_text_metadata()
    {
        Write("poster.jpg");
        await Fetch().GetTextMetadataAsync(Movie());
        await _remote.Received(1).FetchMovieMetadataAsync("Filename title", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_unique_ids_year_sort_title_and_decimal_rating_independent_of_culture()
    {
        Write("movie.nfo", """
            <movie><title>Title</title><sorttitle>Sort key</sorttitle><year>2003</year><rating>7.5</rating>
            <uniqueid type="tmdb">277</uniqueid><uniqueid type="imdb">tt0320691</uniqueid><uniqueid type="tvdb">1154</uniqueid>
            </movie>
            """);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var metadata = Assert.IsType<MetadataResult>((await Fetch().GetTextMetadataAsync(Movie())).Metadata);
            metadata.ReleaseDate.Should().Be(new DateOnly(2003, 1, 1));
            metadata.SortTitle.Should().Be("Sort key");
            metadata.Rating.Should().Be(7.5m);
            metadata.TmdbId.Should().Be("277");
            metadata.ImdbId.Should().Be("tt0320691");
            metadata.TvdbId.Should().Be("1154");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Prefers_moved_local_artwork_and_logo_over_highly_rated_online_artwork()
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var poster = Write("poster.JPG");
        var backdrop = Write("backdrop.jpg");
        var logo = Write("logo.png");
        var remoteArtwork = Substitute.For<IArtworkProvider>();
        remoteArtwork.Id.Returns("tmdb_artwork");

        var artwork = await Fetch(remoteArtwork, new LocalArtworkProvider()).GetArtworkAsync(Movie());

        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Poster && a.Url == poster);
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Backdrop && a.Url == backdrop);
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Logo && a.Url == logo);
        artwork.Should().OnlyContain(a => a.ProviderId == "local_artwork");
        await remoteArtwork.DidNotReceive().GetArtworkAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Online_artwork_fills_only_slots_missing_locally()
    {
        var poster = Write("poster.jpg");
        var remoteArtwork = Substitute.For<IArtworkProvider>();
        remoteArtwork.Id.Returns("tmdb_artwork");
        remoteArtwork.GetArtworkAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ArtworkResult { Kind = Vora.Plugins.Dtos.ArtworkKind.Poster, Url = "https://example.test/poster.jpg", VoteAverage = 10 },
                new ArtworkResult { Kind = Vora.Plugins.Dtos.ArtworkKind.Backdrop, Url = "https://example.test/backdrop.jpg" }
            });

        var artwork = await Fetch(remoteArtwork, new LocalArtworkProvider()).GetArtworkAsync(Movie());

        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Poster && a.Url == poster);
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Backdrop && a.ProviderId == "tmdb_artwork");
    }

    [Fact]
    public async Task Resolves_show_and_season_assets_from_their_episode_files()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Imported show</title><tvdbid>123</tvdbid></tvshow>");
        Write("Show/Season 01/season.nfo", "<season><title>Imported season</title><plot>Season plot</plot></season>");
        var showPoster = Write("Show/poster.jpg");
        var seasonPoster = Write("Show/Season 01/poster.jpg");
        var episodePath = Write("Show/Season 01/Show.S01E01.mkv");
        var library = new MediaLibrary { Name = "TV" };
        var show = new TvShow { Title = "Show", Library = library };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShow = show, Library = library };
        var episodeId = Guid.NewGuid();
        _repository.GetEpisodeFilePathsForShowAsync(show.Id).Returns([episodePath]);
        _repository.GetEpisodeIdsForSeasonAsync(season.Id).Returns([episodeId]);
        _repository.GetMediaFilePathsAsync(episodeId).Returns([episodePath]);
        var fetch = Fetch(new LocalArtworkProvider());

        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(show)).Metadata).Title.Should().Be("Imported show");
        Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(season)).Metadata).Title.Should().Be("Imported season");
        (await fetch.GetArtworkAsync(show)).Should().Contain(a => a.Url == showPoster);
        (await fetch.GetArtworkAsync(season)).Should().Contain(a => a.Url == seasonPoster);
    }

    [Fact]
    public async Task Uses_the_exact_episode_sidecar_and_thumb_without_taking_the_season_poster()
    {
        Write("Show.S01E01.nfo", "<episodedetails><title>Pilot</title><aired>2026-01-02</aired></episodedetails>");
        Write("Show.S01E010.nfo", "<episodedetails><title>Wrong episode</title></episodedetails>");
        Write("poster.jpg");
        var thumb = Write("Show.S01E01-thumb.jpg");
        var episode = new Episode
        {
            Title = "Episode 1", EpisodeNumber = 1, Library = new MediaLibrary { Name = "TV" },
            Season = new Season { Title = "Season 1", SeasonNumber = 1 },
            MediaParts = [new MediaPart { FilePath = Write("Show.S01E01.mkv") }]
        };

        var fetch = Fetch(new LocalArtworkProvider());
        var metadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episode)).Metadata);
        metadata.Title.Should().Be("Pilot");
        metadata.ReleaseDate.Should().Be(new DateOnly(2026, 1, 2));
        (await fetch.GetArtworkAsync(episode)).Should().ContainSingle(a => a.Kind == ArtworkKind.Backdrop && a.Url == thumb);
        Assert.IsType<MetadataResult>(await _local.FetchEpisodeMetadataAsync(_directory, 1, 1, TestContext.Current.CancellationToken)).Title.Should().Be("Pilot");
    }

    [Fact]
    public async Task Imports_metadata_into_voras_model_and_respects_existing_field_locks()
    {
        Write("movie.nfo", "<movie><title>Imported title</title><sorttitle>Sort key</sorttitle><plot>Imported plot</plot><tmdbid>277</tmdbid><genre>Action</genre></movie>");
        var movie = Movie();
        movie.LockField(nameof(movie.Title));
        var pluginSettings = Substitute.For<IPluginSettingsProvider>();
        pluginSettings.GetMetadataLanguageAsync().Returns("eng");
        var mapper = new MetadataMappingService(_repository, Substitute.For<IMediaArtworkRepository>(),
            Substitute.For<IActorRepository>(), Substitute.For<ICollectionRepository>(), Substitute.For<IReferenceRepository>(),
            new ReferenceWriteGate(), Options.Create(new StoragePathsOptions()), pluginSettings);
        var result = await Fetch().GetTextMetadataAsync(movie);

        await mapper.ApplyTextMetadataAsync(movie, Assert.IsType<MetadataResult>(result.Metadata), false, result.ProviderId, result.ProviderName);

        movie.Title.Should().Be("Filename title");
        movie.SortTitle.Should().Be("Sort key");
        movie.Overview.Should().Be("Imported plot");
        movie.TmdbId.Should().Be("277");
        movie.LastMetadataRefresh.Should().NotBeNull();
        await _repository.Received(1).SetMediaGenresAsync(movie.Id, Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 28 })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Library_and_single_file_scans_ingest_the_nfo_identity(bool singleFile)
    {
        Fixture("EmbyMovie.nfo", "movie.nfo");
        var file = Write("unhelpful-filename.mkv");
        var ingestion = Substitute.For<IMediaIngestionService>();
        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        ingestion.GetLibraryDetailsAsync(library).Returns((new List<string> { _directory }, null, new List<string>()));
        ingestion.GetExistingLibraryPathsAsync(library).Returns(new HashSet<string>());
        ingestion.EnsureMovieAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new MediaItemHandle(Guid.NewGuid()));
        var scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, ingestion, new NullTaskProgressReporter());

        if (singleFile) await scanner.ScanMovieFileAsync(library.Value, file);
        else await scanner.ScanMovieLibraryAsync(library.Value);

        await ingestion.Received(1).EnsureMovieAsync(library, "Underworld", 2003, "277", "tt0320691", "1154", null);
        await ingestion.Received(1).AddMediaPartAsync(Arg.Any<MediaItemHandle>(), file, null, null);
    }

    [Fact]
    public async Task Tv_scan_imports_show_identity_and_episode_title_and_air_date()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Imported show</title><year>2025</year><uniqueid type='tvdb'>123</uniqueid></tvshow>");
        Write("Show/Season 01/Show.S01E01.nfo", "<episodedetails><title>Pilot</title><aired>2025-06-01</aired></episodedetails>");
        var file = Write("Show/Season 01/Show.S01E01.mkv");
        var ingestion = Substitute.For<IMediaIngestionService>();
        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        var showId = new MediaItemHandle(Guid.NewGuid());
        var seasonId = new SeasonHandle(Guid.NewGuid());
        ingestion.GetLibraryDetailsAsync(library).Returns((new List<string> { _directory }, null, new List<string>()));
        ingestion.GetExistingLibraryPathsAsync(library).Returns(new HashSet<string>());
        ingestion.EnsureTvShowAsync(library, "Imported show", 2025, null, null, "123").Returns(showId);
        ingestion.EnsureSeasonAsync(library, showId, 1).Returns(seasonId);
        var scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, ingestion, new NullTaskProgressReporter());

        await scanner.ScanTvShowLibraryAsync(library.Value);

        await ingestion.Received(1).EnsureTvShowAsync(library, "Imported show", 2025, null, null, "123");
        await ingestion.Received(1).EnsureEpisodeAsync(library, seasonId, 1, "Pilot", new DateOnly(2025, 6, 1), null, null);
    }

    [Fact]
    public async Task Does_not_match_episode_one_to_episode_ten()
    {
        Write("Show.S01E010.nfo", "<episodedetails><title>Wrong episode</title></episodedetails>");
        (await _local.FetchEpisodeMetadataAsync(_directory, 1, 1, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Chooses_file_specific_artwork_and_ignores_non_image_files()
    {
        Write("poster.nfo", "<movie><title>Not an image</title></movie>");
        var sharedPoster = Write("poster.jpg");
        var ownPoster = Write("First-poster.png");
        var ownLogo = Write("First-logo.png");
        var fetch = Fetch(new LocalArtworkProvider());

        var artwork = await fetch.GetArtworkAsync(Movie("First.mkv"));

        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Poster && a.Url == ownPoster);
        artwork.Should().ContainSingle(a => a.Kind == ArtworkKind.Logo && a.Url == ownLogo);
        artwork.Should().NotContain(a => a.Url == sharedPoster || a.Url.EndsWith(".nfo"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Imports_the_FROM_show_and_all_ten_episodes_from_the_supplied_season_layout(bool singleFile)
    {
        const string folder = "From (2022) Season 1 S01 (1080p AMZN WEB-DL x265 HEVC 10bit EAC3 5.1 t3nzin)";
        var sampleDirectory = Path.Combine(_directory, folder);
        Directory.CreateDirectory(sampleDirectory);
        var source = Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", "From");
        foreach (var nfo in Directory.GetFiles(source, "*.nfo"))
            File.Copy(nfo, Path.Combine(sampleDirectory, Path.GetFileName(nfo)));
        var episodeNfos = Directory.GetFiles(sampleDirectory, "*.nfo")
            .Where(path => Path.GetFileName(path) != "tvshow.nfo").Order(StringComparer.Ordinal).ToList();
        episodeNfos.Should().HaveCount(10);
        var episodes = episodeNfos.Select((nfo, index) => new Episode
        {
            Title = "Filename title", EpisodeNumber = index + 1,
            Library = new MediaLibrary { Name = "TV" },
            MediaParts = [new MediaPart { FilePath = Write(Path.Combine(folder, Path.GetFileNameWithoutExtension(nfo) + ".mkv")) }]
        }).ToList();
        var poster = Write(Path.Combine(folder, "poster.jpg"));
        var backdrop = Write(Path.Combine(folder, "fanart.jpg"));
        var logo = Write(Path.Combine(folder, "clearlogo.png"));
        var seasonPoster = Write(Path.Combine(folder, "season01-poster.jpg"));
        foreach (var episode in episodes)
            Write(Path.Combine(folder, Path.GetFileNameWithoutExtension(episode.MediaParts.Single().FilePath) + "-thumb.jpg"));
        var show = new TvShow { Title = "Filename show", Library = new MediaLibrary { Name = "TV" } };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShow = show, Library = show.Library };
        foreach (var episode in episodes) episode.Season = season;
        _repository.GetEpisodeFilePathsForShowAsync(show.Id).Returns(episodes.Select(episode => episode.MediaParts.Single().FilePath).ToList());
        _repository.GetEpisodeIdsForSeasonAsync(season.Id).Returns(episodes.Select(episode => episode.Id).ToList());
        _repository.GetMediaFilePathsAsync(episodes[0].Id).Returns([episodes[0].MediaParts.Single().FilePath]);
        var fetch = Fetch(new LocalArtworkProvider());

        var showMetadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(show)).Metadata);
        showMetadata.Title.Should().Be("FROM");
        showMetadata.Overview.Should().Contain("nightmarish town");
        showMetadata.TmdbId.Should().Be("124364");
        showMetadata.ImdbId.Should().Be("tt9813792");
        showMetadata.TvdbId.Should().Be("401003");
        showMetadata.ReleaseDate.Should().Be(new DateOnly(2022, 2, 20));
        showMetadata.ContentRating.Should().Be("TV-MA");
        showMetadata.Status.Should().Be("Continuing");
        showMetadata.Cast.Should().Contain(actor => actor.Name == "Harold Perrineau" && actor.CharacterName == "Boyd Stevens"
            && actor.TmdbId == 6195 && actor.TvdbId == 7933614);
        var actorRepository = Substitute.For<IActorRepository>();
        actorRepository.GetActorsByTmdbIdsOrNamesAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<IEnumerable<string>>())
            .Returns(new List<Vora.Domain.Entities.Actors.Actor>());
        var pluginSettings = Substitute.For<IPluginSettingsProvider>();
        pluginSettings.GetMetadataLanguageAsync().Returns("eng");
        var mapper = new MetadataMappingService(_repository, Substitute.For<IMediaArtworkRepository>(),
            actorRepository, Substitute.For<ICollectionRepository>(), Substitute.For<IReferenceRepository>(),
            new ReferenceWriteGate(), Options.Create(new StoragePathsOptions()), pluginSettings);
        await mapper.ApplyTextMetadataAsync(show, showMetadata, false, "local_metadata", "Local");
        show.Title.Should().Be("FROM");
        show.ContentRating.Should().Be("TV-MA");
        var showArtwork = await fetch.GetArtworkAsync(show);
        showArtwork.Should().Contain(a => a.Kind == ArtworkKind.Poster && a.Url == poster);
        showArtwork.Should().Contain(a => a.Kind == ArtworkKind.Backdrop && a.Url == backdrop);
        showArtwork.Should().Contain(a => a.Kind == ArtworkKind.Logo && a.Url == logo);
        (await fetch.GetArtworkAsync(season)).Should().ContainSingle(a => a.Kind == ArtworkKind.Poster && a.Url == seasonPoster);

        var expectedTitles = new[]
        {
            "Long Day's Journey Into Night", "The Way Things Are Now", "Choosing Day", "A Rock and a Farway",
            "Silhouettes", "Book 74", "All Good Things", "Broken Windows, Open Doors", "Into the Woods", "Oh, the Places We'll Go"
        };
        var ingestion = Substitute.For<IMediaIngestionService>();
        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        var showId = new MediaItemHandle(show.Id);
        var seasonId = new SeasonHandle(season.Id);
        ingestion.GetLibraryDetailsAsync(library).Returns((new List<string> { _directory }, null, new List<string>()));
        ingestion.GetExistingLibraryPathsAsync(library).Returns(new HashSet<string>());
        ingestion.EnsureTvShowAsync(library, "FROM", 2022, "124364", "tt9813792", "401003").Returns(showId);
        ingestion.EnsureSeasonAsync(library, showId, 1).Returns(seasonId);
        var scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, ingestion, new NullTaskProgressReporter());
        if (singleFile)
        {
            foreach (var episode in episodes) await scanner.ScanTvFileAsync(library.Value, episode.MediaParts.Single().FilePath);
        }
        else await scanner.ScanTvShowLibraryAsync(library.Value);

        for (var index = 0; index < episodes.Count; index++)
        {
            var episode = episodes[index];
            var metadata = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episode)).Metadata);
            metadata.Title.Should().Be(expectedTitles[index]);
            metadata.Overview.Should().NotBeNullOrWhiteSpace();
            metadata.ReleaseDate.Should().NotBeNull();
            await mapper.ApplyTextMetadataAsync(episode, metadata, false, "local_metadata", "Local");
            episode.ContentRating.Should().Be("TV-MA");
            episode.Title.Should().Be(expectedTitles[index]);
            metadata.TvdbId.Should().NotBeNullOrWhiteSpace();
            var thumb = Path.Combine(sampleDirectory, Path.GetFileNameWithoutExtension(episode.MediaParts.Single().FilePath) + "-thumb.jpg");
            (await fetch.GetArtworkAsync(episode)).Should().ContainSingle(a => a.Kind == ArtworkKind.Backdrop && a.Url == thumb);
            await ingestion.Received(1).EnsureEpisodeAsync(library, seasonId, index + 1, expectedTitles[index], metadata.ReleaseDate, null, null);
        }
        var pilot = Assert.IsType<MetadataResult>((await fetch.GetTextMetadataAsync(episodes[0])).Metadata);
        pilot.ReleaseDate.Should().Be(new DateOnly(2022, 2, 20));
        pilot.RuntimeMinutes.Should().Be(52);
        pilot.Cast.Should().Contain(person => person.Name == "Jack Bender" && person.Roles == CastRole.Director
            && person.TmdbId == 28976 && person.TvdbId == 248008);
    }

    [Fact]
    public void A_show_folder_that_starts_with_Season_keeps_its_own_tvshow_nfo()
    {
        Write("Season One/tvshow.nfo", "<tvshow><title>Season One</title></tvshow>");
        var path = Write("Season One/Season.One.S01E01.mkv");
        LocalMediaAssets.ShowDirectory(path).Should().Be(Path.GetDirectoryName(path));
    }

    [Fact]
    public async Task Does_not_assign_a_flat_show_poster_to_a_season_without_its_own_poster()
    {
        Write("tvshow.nfo", "<tvshow><title>Show</title></tvshow>");
        Write("poster.jpg");
        var episodePath = Write("Show.S01E01.mkv");
        var season = new Season { Title = "Season 1", SeasonNumber = 1, Library = new MediaLibrary { Name = "TV" } };
        var episodeId = Guid.NewGuid();
        _repository.GetEpisodeIdsForSeasonAsync(season.Id).Returns([episodeId]);
        _repository.GetMediaFilePathsAsync(episodeId).Returns([episodePath]);

        (await Fetch(new LocalArtworkProvider()).GetArtworkAsync(season)).Should().NotContain(a => a.Kind == ArtworkKind.Poster);
    }

    [Fact]
    public async Task Reads_a_show_level_season_poster_for_an_episode_in_a_season_subfolder()
    {
        Write("Show/tvshow.nfo", "<tvshow><title>Show</title></tvshow>");
        var poster = Write("Show/season02-poster.jpg");
        var episodePath = Write("Show/Season 02/Show.S02E01.mkv");
        var season = new Season { Title = "Season 2", SeasonNumber = 2, Library = new MediaLibrary { Name = "TV" } };
        var episodeId = Guid.NewGuid();
        _repository.GetEpisodeIdsForSeasonAsync(season.Id).Returns([episodeId]);
        _repository.GetMediaFilePathsAsync(episodeId).Returns([episodePath]);

        (await Fetch(new LocalArtworkProvider()).GetArtworkAsync(season)).Should().ContainSingle(a => a.Kind == ArtworkKind.Poster && a.Url == poster);
    }
}
