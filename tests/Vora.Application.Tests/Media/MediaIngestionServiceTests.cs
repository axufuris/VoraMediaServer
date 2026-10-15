using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Requests;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Media;

public class MediaIngestionServiceTests : IDisposable
{
    private readonly string _tempArtwork;
    private readonly IMediaRepository _mediaRepo;
    private readonly ILibraryRepository _libraryRepo;
    private readonly IRequestManager _requestManager;
    private readonly IMusicRepository _musicRepo;
    private readonly ITaskQueueManager _taskQueue;
    private readonly IMediaAnalyzerService _analyzerService;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly MediaIngestionService _service;

    public MediaIngestionServiceTests()
    {
        _tempArtwork = Path.Combine(Path.GetTempPath(), "vora-ingestion-tests-" + Guid.NewGuid().ToString("N"));

        _mediaRepo = Substitute.For<IMediaRepository>();
        _libraryRepo = Substitute.For<ILibraryRepository>();
        _requestManager = Substitute.For<IRequestManager>();
        _musicRepo = Substitute.For<IMusicRepository>();
        _taskQueue = Substitute.For<ITaskQueueManager>();
        _analyzerService = Substitute.For<IMediaAnalyzerService>();
        _settingsRepo = Substitute.For<ISystemSettingsRepository>();
        _adminNotifications = Substitute.For<Vora.Application.Notifications.IAdminNotificationManager>();
        _settingsRepo.GetSettingsAsync().Returns(new Vora.Domain.Entities.Settings.ServerSetting { ScanIgnoredFolders = new List<string>() });

        var options = Options.Create(new StoragePathsOptions { CustomArtwork = _tempArtwork });

        _service = new MediaIngestionService(
            _mediaRepo,
            _libraryRepo,
            _requestManager,
            _musicRepo,
            _taskQueue,
            _analyzerService,
            new ReferenceWriteGate(),
            _settingsRepo,
            _adminNotifications,
            options,
            NullLogger<MediaIngestionService>.Instance);
    }

    private readonly Vora.Application.Notifications.IAdminNotificationManager _adminNotifications;

    [Fact]
    public async Task Files_a_scan_left_out_raise_one_notice_per_library_until_it_is_read()
    {
        var libraryId = Guid.NewGuid();
        _libraryRepo.GetProjectedByIdAsync(libraryId, Arg.Any<System.Linq.Expressions.Expression<Func<Vora.Domain.Entities.Library.MediaLibrary, string>>>()).Returns("TV Shows");

        await _service.ReportUnreadableEpisodeFilesAsync(LibraryHandle.FromGuid(libraryId), ["/tv/Show/Season 1/Bonus Material.mkv", "/tv/Show/Season 1/Extra Bit.mkv"]);

        await _adminNotifications.Received(1).RaiseAsync(
            Vora.Domain.Enums.AdminNotificationSeverity.Warning,
            "Some episode files weren't added",
            Arg.Is<string>(message => message.StartsWith("2 files in TV Shows weren't added") && message.Contains("\"Bonus Material.mkv\"") && message.Contains("Check my files")),
            $"{{\"kind\":\"unreadable-episode-files\",\"libraryId\":\"{libraryId}\"}}",
            true);
    }

    [Fact]
    public async Task Nothing_left_out_raises_nothing()
    {
        await _service.ReportUnreadableEpisodeFilesAsync(LibraryHandle.FromGuid(Guid.NewGuid()), []);

        await _adminNotifications.DidNotReceive().RaiseAsync(Arg.Any<Vora.Domain.Enums.AdminNotificationSeverity>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempArtwork))
        {
            Directory.Delete(_tempArtwork, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureMovieAsync_returns_existing_handle_when_movie_already_exists()
    {
        var existingId = Guid.NewGuid();
        _mediaRepo.GetMovieIdByTitleAndYearAsync("Inception", 2010, Arg.Any<Guid>())
            .Returns((Guid?)existingId);

        var handle = await _service.EnsureMovieAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "Inception", 2010, null, null);

        handle.Value.Should().Be(existingId);
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task EnsureMovieAsync_looks_up_the_title_with_the_files_ids_so_another_film_of_that_name_is_not_taken()
    {
        await _service.EnsureMovieAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "The Princess", 2022, null, "tt13393128");

        await _mediaRepo.Received(1).GetMovieIdByTitleAndYearAsync("The Princess", 2022, Arg.Any<Guid>(), null, "tt13393128");
        await _mediaRepo.Received(1).AddMediaItemAsync(Arg.Is<Movie>(m => m.ImdbId == "tt13393128"));
    }

    [Fact]
    public async Task EnsureMovieAsync_creates_new_movie_when_absent()
    {
        _mediaRepo.GetMovieIdByTitleAndYearAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);

        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        var handle = await _service.EnsureMovieAsync(library, "Dune", 2021, tmdbId: "438631", imdbId: null);

        await _mediaRepo.Received(1).AddMediaItemAsync(Arg.Is<Movie>(m =>
            m.Title == "Dune" &&
            m.LibraryId == library.Value &&
            m.TmdbId == "438631" &&
            m.ReleaseDate!.Value.Year == 2021));

        handle.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task EnsureMovieAsync_triggers_request_resolution_when_tmdb_id_supplied()
    {
        _mediaRepo.GetMovieIdByTitleAndYearAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);

        await _service.EnsureMovieAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "Dune", 2021, "438631", null);

        await _requestManager.Received(1).ResolveRequestAsync("438631", "Movie", Arg.Any<Guid>());
    }

    [Fact]
    public async Task EnsureMovieAsync_does_not_call_request_manager_when_no_tmdb_id()
    {
        _mediaRepo.GetMovieIdByTitleAndYearAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);

        await _service.EnsureMovieAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "Unknown Movie", null, tmdbId: null, imdbId: null);

        await _requestManager.DidNotReceive().ResolveRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task EnsureTvShowAsync_dedups_by_title()
    {
        var existing = Guid.NewGuid();
        _mediaRepo.GetTvShowIdByTitleAndYearAsync("Breaking Bad", 2008, Arg.Any<Guid>())
            .Returns((Guid?)existing);

        var handle = await _service.EnsureTvShowAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "Breaking Bad", 2008, null, null);

        handle.Value.Should().Be(existing);
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task EnsureTvShowAsync_dedups_by_external_id_before_title()
    {
        var existing = Guid.NewGuid();
        _mediaRepo.GetTvShowIdByExternalIdAsync("1396", null, Arg.Any<Guid>())
            .Returns((Guid?)existing);

        var handle = await _service.EnsureTvShowAsync(LibraryHandle.FromGuid(Guid.NewGuid()), "Breaking Bad", 2008, tmdbId: "1396", imdbId: null);

        handle.Value.Should().Be(existing);
        await _mediaRepo.DidNotReceive().GetTvShowIdByTitleAndYearAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid>());
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task EnsureTvShowAsync_creates_new_show_when_no_id_or_title_match()
    {
        _mediaRepo.GetTvShowIdByExternalIdAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);
        _mediaRepo.GetTvShowIdByTitleAndYearAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);

        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        await _service.EnsureTvShowAsync(library, "Severance", 2022, tmdbId: "95396", imdbId: null);

        await _mediaRepo.Received(1).AddMediaItemAsync(Arg.Is<TvShow>(t =>
            t.Title == "Severance" &&
            t.TmdbId == "95396" &&
            t.LibraryId == library.Value));
        await _requestManager.Received(1).ResolveRequestAsync("95396", "TvShow", Arg.Any<Guid>());
    }

    [Fact]
    public async Task EnsureSeasonAsync_returns_existing_handle_when_season_already_exists()
    {
        var existing = Guid.NewGuid();
        _mediaRepo.GetSeasonIdByNumberAsync(Arg.Any<Guid>(), 1)
            .Returns((Guid?)existing);

        var handle = await _service.EnsureSeasonAsync(
            LibraryHandle.FromGuid(Guid.NewGuid()),
            new MediaItemHandle(Guid.NewGuid()),
            1);

        handle.Value.Should().Be(existing);
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task EnsureSeasonAsync_creates_new_season_with_correct_show_link()
    {
        _mediaRepo.GetSeasonIdByNumberAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns((Guid?)null);

        var library = LibraryHandle.FromGuid(Guid.NewGuid());
        var showHandle = new MediaItemHandle(Guid.NewGuid());

        await _service.EnsureSeasonAsync(library, showHandle, 3);

        await _mediaRepo.Received(1).AddMediaItemAsync(Arg.Is<Season>(s =>
            s.SeasonNumber == 3 &&
            s.TvShowId == showHandle.Value &&
            s.LibraryId == library.Value));
    }

    private Track GivenExistingTrack(string? rating, string? provider = null, string? isrc = null)
    {
        var track = new Track { Id = Guid.NewGuid(), Title = "White America", TrackNumber = 2, ContentRating = rating, ContentRatingProvider = provider, Isrc = isrc };
        _musicRepo.GetTracksByAlbumAndNumberAsync(Arg.Any<Guid>(), 2, Arg.Any<int?>()).Returns([track]);
        return track;
    }

    private Task<MediaItemHandle> EnsureTrackAsync(string title, string? isrc = null) => _service.EnsureTrackAsync(
        LibraryHandle.FromGuid(Guid.NewGuid()), new AlbumHandle(Guid.NewGuid()), title, 2, null, 324, "flac", 44100, 900, null, isrc: isrc);

    [Fact]
    public async Task Another_song_with_the_same_track_number_becomes_its_own_track()
    {
        var circus = GivenExistingTrack(null);
        circus.Title = "Circus";

        var handle = await EnsureTrackAsync("Circus (acoustic version)");

        handle.Value.Should().NotBe(circus.Id);
        await _mediaRepo.Received(1).AddMediaItemAsync(Arg.Is<Track>(t => t.Title == "Circus (acoustic version)" && t.TrackNumber == 2));
    }

    [Fact]
    public async Task The_same_song_in_another_file_joins_its_track_even_with_other_punctuation()
    {
        var song = GivenExistingTrack(null);
        song.Title = "Don’t Push";

        (await EnsureTrackAsync("Don't Push")).Value.Should().Be(song.Id);
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task A_track_renamed_by_hand_still_takes_another_copy_of_its_song()
    {
        var song = GivenExistingTrack(null);
        song.Title = "Wrong Way (Live in Long Beach)";
        song.OriginalTitle = "Wrong Way";

        (await EnsureTrackAsync("Wrong Way")).Value.Should().Be(song.Id);
    }

    [Fact]
    public async Task A_matching_isrc_is_the_same_recording_whatever_the_title_says()
    {
        var song = GivenExistingTrack(null, isrc: "USUM71700626");
        song.Title = "Sober";

        (await EnsureTrackAsync("Sober (radio edit)", isrc: "US-UM7-17-00626")).Value.Should().Be(song.Id);
    }

    private Album GivenAlbum(string title, params string[] files)
    {
        var album = new Album { Id = Guid.NewGuid(), Title = title, ArtistId = Guid.NewGuid() };
        _albumFiles.AddRange(files.Select(file => (album.Id, file)));
        _albums.Add(album);
        return album;
    }

    private string Music(params string[] parts) => Path.Combine([_tempArtwork, "music", .. parts]);

    private string OnDisk(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? _tempArtwork);
        File.WriteAllBytes(file, []);
        return file;
    }

    private readonly List<Album> _albums = [];
    private readonly List<(Guid AlbumId, string FilePath)> _albumFiles = [];

    private Task<AlbumHandle> EnsureAlbumAsync(string title, params string[] folders)
    {
        _musicRepo.GetAlbumsByTitleAsync(Arg.Any<Guid>(), Arg.Any<string>()).Returns(_ => _albums.Where(a => a.Title.Equals(title, StringComparison.OrdinalIgnoreCase)).ToList());
        _musicRepo.GetAlbumFilePathsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(_ => _albumFiles.ToList());
        return _service.EnsureAlbumAsync(LibraryHandle.FromGuid(Guid.NewGuid()), new ArtistHandle(Guid.NewGuid()), title, 1994, null, null, null,
            albumFolders: folders.Length == 0 ? null : folders);
    }

    [Fact]
    public async Task An_album_is_the_one_whose_files_are_in_the_folder()
    {
        GivenAlbum("Weezer", OnDisk(Music("Weezer", "Weezer (Teal Album)", "01.flac")));
        var blue = GivenAlbum("Weezer", OnDisk(Music("Weezer", "Weezer (Blue Album)", "11.flac")));

        (await EnsureAlbumAsync("Weezer", Music("Weezer", "Weezer (Blue Album)"))).Value.Should().Be(blue.Id);
        await _musicRepo.DidNotReceive().AddAlbumAsync(Arg.Any<Album>());
    }

    [Fact]
    public async Task An_album_of_the_same_name_in_another_folder_is_a_new_album()
    {
        GivenAlbum("Weezer", OnDisk(Music("Weezer", "Weezer (Teal Album)", "01.flac")));

        await EnsureAlbumAsync("Weezer", Music("Weezer", "Weezer (Green Album)"));

        await _musicRepo.Received(1).AddAlbumAsync(Arg.Is<Album>(a => a.Title == "Weezer"));
    }

    [Fact]
    public async Task An_album_with_no_files_left_is_used_again()
    {
        var empty = GivenAlbum("Positions");

        (await EnsureAlbumAsync("Positions", Music("Ariana Grande", "Positions"))).Value.Should().Be(empty.Id);
    }

    [Fact]
    public async Task An_album_whose_folder_was_renamed_is_used_again_before_its_old_files_are_cleared()
    {
        var renamed = GivenAlbum("Pinkerton", Music("Weezer", "Pinkerton", "01.flac"));

        (await EnsureAlbumAsync("Pinkerton", Music("Weezer", "Pinkerton (1996)"))).Value.Should().Be(renamed.Id);
        await _musicRepo.DidNotReceive().AddAlbumAsync(Arg.Any<Album>());
    }

    [Fact]
    public async Task An_existing_album_gets_the_year_it_was_missing()
    {
        var album = GivenAlbum("Weezer", OnDisk(Music("Weezer", "Weezer (Blue Album)", "11.flac")));

        await EnsureAlbumAsync("Weezer", Music("Weezer", "Weezer (Blue Album)"));

        album.Year.Should().Be(1994);
    }

    [Fact]
    public async Task Forgetting_a_split_albums_art_and_year_keeps_what_was_locked_and_fetches_art_again()
    {
        var album = new Album { Id = Guid.NewGuid(), Title = "Weezer", ArtworkUrl = "/art/teal.jpg", BackgroundUrl = "/art/bg.jpg", Year = 2019 };
        album.LockedFields.Add(nameof(Album.BackgroundUrl));
        _musicRepo.GetAlbumForUpdateAsync(album.Id).Returns(album);

        await _service.ForgetAlbumArtAndYearAsync(album.Id);

        album.ArtworkUrl.Should().BeNull();
        album.Year.Should().BeNull();
        album.BackgroundUrl.Should().Be("/art/bg.jpg");
        _taskQueue.Received(1).QueueRefreshAlbumArtwork(album.Id, "Weezer");
    }

    private Task RescanAsync(string? rating, string? isrc = null) => _service.EnsureTrackAsync(
        LibraryHandle.FromGuid(Guid.NewGuid()), new AlbumHandle(Guid.NewGuid()), "White America", 2, null, 324,
        "flac", 44100, 900, rating, isrc: isrc);

    // The tag describes this file; a provider only described a recording it matched.
    [Fact]
    public async Task A_file_advisory_tag_replaces_a_provider_rating()
    {
        var track = GivenExistingTrack("Clean", provider: "deezer_content_ratings");

        await RescanAsync("Explicit");

        track.ContentRating.Should().Be("Explicit");
        track.ContentRatingProvider.Should().BeNull();
    }

    [Fact]
    public async Task A_file_with_no_advisory_tag_keeps_the_provider_rating()
    {
        var track = GivenExistingTrack("Explicit", provider: "deezer_content_ratings");

        await RescanAsync(null);

        track.ContentRating.Should().Be("Explicit");
        track.ContentRatingProvider.Should().Be("deezer_content_ratings");
    }

    [Fact]
    public async Task A_locked_rating_survives_a_file_tag()
    {
        var track = GivenExistingTrack("Clean");
        track.LockField(nameof(Track.ContentRating));

        await RescanAsync("Explicit");

        track.ContentRating.Should().Be("Clean");
    }

    // A new ISRC is a different recording — the file was replaced with another
    // edition — so the provider's answer about the old one is dropped and asked again.
    [Fact]
    public async Task A_changed_isrc_drops_the_provider_rating_and_asks_again()
    {
        var track = GivenExistingTrack("Explicit", provider: "deezer_content_ratings", isrc: "USIR10211052");
        track.ContentRatingCheckedAt = DateTime.UtcNow;

        await RescanAsync(null, isrc: "USIR10211126");

        track.Isrc.Should().Be("USIR10211126");
        track.ContentRating.Should().BeNull();
        track.ContentRatingCheckedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("us-ir1-02-11052", "USIR10211052")]
    [InlineData(" USIR10211052 ", "USIR10211052")]
    [InlineData("not an isrc", null)]
    [InlineData("", null)]
    public void Isrcs_are_stored_in_one_form_or_not_at_all(string raw, string? expected)
    {
        MediaIngestionService.NormalizeIsrc(raw).Should().Be(expected);
    }
}
