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

        var options = Options.Create(new StoragePathsOptions { CustomArtwork = _tempArtwork });

        _service = new MediaIngestionService(
            _mediaRepo,
            _libraryRepo,
            _requestManager,
            _musicRepo,
            _taskQueue,
            _analyzerService,
            new ReferenceWriteGate(),
            options,
            NullLogger<MediaIngestionService>.Instance);
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
        _mediaRepo.GetTvShowIdByTitleAsync("Breaking Bad", Arg.Any<Guid>())
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
        await _mediaRepo.DidNotReceive().GetTvShowIdByTitleAsync(Arg.Any<string>(), Arg.Any<Guid>());
        await _mediaRepo.DidNotReceive().AddMediaItemAsync(Arg.Any<MediaItem>());
    }

    [Fact]
    public async Task EnsureTvShowAsync_creates_new_show_when_no_id_or_title_match()
    {
        _mediaRepo.GetTvShowIdByExternalIdAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid>())
            .Returns((Guid?)null);
        _mediaRepo.GetTvShowIdByTitleAsync(Arg.Any<string>(), Arg.Any<Guid>())
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

}
