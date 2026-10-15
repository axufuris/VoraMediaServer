using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class LocalMetadataScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-nfo-scan-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaIngestionService _ingestion = Substitute.For<IMediaIngestionService>();
    private readonly LibraryHandle _library = LibraryHandle.FromGuid(Guid.NewGuid());
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly VoraLocalMediaScannerProvider _scanner;

    public LocalMetadataScanTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((new List<string> { _root }, null, new List<string>())));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(_known);
        _ingestion.GetFirstEpisodeFilePathsAsync(Arg.Any<LibraryHandle>()).Returns(new List<string>());
        _ingestion.EnsureMovieAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
        _ingestion.EnsureTvShowAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
        _ingestion.EnsureSeasonAsync(Arg.Any<LibraryHandle>(), Arg.Any<MediaItemHandle>(), Arg.Any<int>())
            .Returns(_ => new SeasonHandle(Guid.NewGuid()));
        _ingestion.EnsureEpisodeAsync(Arg.Any<LibraryHandle>(), Arg.Any<SeasonHandle>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DateOnly?>(), Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Reads(LocalMetadataMode mode) => _ingestion.GetLocalMetadataModeAsync(Arg.Any<LibraryHandle>()).Returns(mode);

    private string Write(string relative, string contents = "")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        var parent = Path.GetDirectoryName(path);
        if (parent != null) Directory.CreateDirectory(parent);
        File.WriteAllText(path, contents);
        return path;
    }

    private const string UnderworldNfo = "<movie><title>Underworld</title><year>2003</year><tmdbid>277</tmdbid><imdbid>tt0320691</imdbid></movie>";

    [Fact]
    public async Task A_library_that_does_not_read_nfo_files_names_a_film_from_its_file()
    {
        Reads(LocalMetadataMode.Off);
        Write("Underworld (2003)/movie.nfo", UnderworldNfo);
        Write("Underworld (2003)/Underwrld (2003).mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Underwrld", 2003, null, null, null, null);
    }

    [Theory]
    [InlineData(LocalMetadataMode.MatchOnly, false)]
    [InlineData(LocalMetadataMode.Prefer, false)]
    [InlineData(LocalMetadataMode.Prefer, true)]
    public async Task A_library_that_reads_nfo_files_takes_the_films_identity_from_one(LocalMetadataMode mode, bool singleFile)
    {
        Reads(mode);
        Write("Underworld (2003)/movie.nfo", UnderworldNfo);
        var file = Write("Underworld (2003)/unhelpful-filename.mkv");

        if (singleFile) await _scanner.ScanMovieFileAsync(_library.Value, file);
        else await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Underworld", 2003, "277", "tt0320691", null, null);
        await _ingestion.Received(1).AddMediaPartAsync(Arg.Any<MediaItemHandle>(), file, null, null);
    }

    [Fact]
    public async Task One_movie_nfo_in_a_folder_of_several_films_names_none_of_them()
    {
        Reads(LocalMetadataMode.MatchOnly);
        Write("movie.nfo", UnderworldNfo);
        Write("Alien (1979).mkv");
        Write("Heat (1995).mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Alien", 1979, null, null, null, null);
        await _ingestion.Received(1).EnsureMovieAsync(_library, "Heat", 1995, null, null, null, null);
        await _ingestion.DidNotReceive().EnsureMovieAsync(_library, "Underworld", Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task An_id_in_the_file_name_beats_a_different_id_in_the_nfo()
    {
        Reads(LocalMetadataMode.MatchOnly);
        Write("Underworld (2003)/Underworld (2003) {tmdb-999}.nfo", UnderworldNfo);
        Write("Underworld (2003)/Underworld (2003) {tmdb-999}.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Underworld", 2003, "999", null, null, null);
    }

    [Fact]
    public async Task An_nfo_for_a_film_from_another_year_is_ignored()
    {
        Reads(LocalMetadataMode.MatchOnly);
        Write("Heat (1995)/movie.nfo", UnderworldNfo);
        Write("Heat (1995)/Heat (1995).mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Heat", 1995, null, null, null, null);
    }

    [Theory]
    [InlineData(LocalMetadataMode.Off)]
    [InlineData(LocalMetadataMode.Prefer)]
    public async Task A_new_version_of_a_film_matched_by_hand_joins_that_film(LocalMetadataMode mode)
    {
        Reads(mode);
        Write("Underworld (2003)/movie.nfo", UnderworldNfo);
        var known = Write("Underworld (2003)/Underworld (2003) - 1080p.mkv");
        var added = Write("Underworld (2003)/Underworld (2003) - 4K.mkv");
        _known.Add(known);
        var matchedByHand = new MediaItemHandle(Guid.NewGuid());
        _ingestion.FindHandMatchedOwnerAsync(_library, known).Returns(matchedByHand);

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).AddMediaPartAsync(matchedByHand, added, Arg.Any<string?>(), Arg.Any<string?>());
        await _ingestion.DidNotReceive().EnsureMovieAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task A_different_film_beside_one_matched_by_hand_is_still_its_own_film()
    {
        Reads(LocalMetadataMode.Off);
        var known = Write("Alien (1979).mkv");
        Write("Heat (1995).mkv");
        _known.Add(known);
        _ingestion.FindHandMatchedOwnerAsync(_library, known).Returns(new MediaItemHandle(Guid.NewGuid()));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Heat", 1995, null, null, null, null);
    }

    [Fact]
    public async Task A_trailer_is_attached_to_the_film_under_the_name_its_nfo_gave_it()
    {
        Reads(LocalMetadataMode.MatchOnly);
        Write("Underwrld/movie.nfo", UnderworldNfo);
        Write("Underwrld/Underwrld.mkv");
        var trailer = Write("Underwrld/Trailers/Teaser.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).AttachLocalExtraAsync(_library, "Underworld", 2003, trailer, "Trailer", "Teaser");
    }

    private const string ShowNfo = "<tvshow><title>Imported show</title><year>2025</year><uniqueid type='tvdb'>123</uniqueid></tvshow>";
    private const string EpisodeNfo = "<episodedetails><title>Pilot</title><aired>2025-06-01</aired></episodedetails>";

    [Fact]
    public async Task A_tv_library_that_does_not_read_nfo_files_names_everything_from_the_folders()
    {
        Reads(LocalMetadataMode.Off);
        Write("Show/tvshow.nfo", ShowNfo);
        Write("Show/Season 01/Show.S01E01.nfo", EpisodeNfo);
        Write("Show/Season 01/Show.S01E01.mkv");

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(_library, "Show", null, null, null, null);
        await _ingestion.Received(1).EnsureEpisodeAsync(_library, Arg.Any<SeasonHandle>(), 1, "Show - S01E01", null, null, null);
    }

    [Fact]
    public async Task Matching_only_takes_the_shows_identity_from_the_nfo_and_leaves_episode_names_to_the_provider()
    {
        Reads(LocalMetadataMode.MatchOnly);
        Write("Show/tvshow.nfo", ShowNfo);
        Write("Show/Season 01/Show.S01E01.nfo", EpisodeNfo);
        Write("Show/Season 01/Show.S01E01.mkv");

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(_library, "Imported show", 2025, null, null, "123");
        await _ingestion.Received(1).EnsureEpisodeAsync(_library, Arg.Any<SeasonHandle>(), 1, "Imported show - S01E01", null, null, null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preferring_the_nfo_also_takes_each_episodes_title_and_air_date(bool singleFile)
    {
        Reads(LocalMetadataMode.Prefer);
        Write("Show/tvshow.nfo", ShowNfo);
        Write("Show/Season 01/Show.S01E01.nfo", EpisodeNfo);
        var file = Write("Show/Season 01/Show.S01E01.mkv");

        if (singleFile) await _scanner.ScanTvFileAsync(_library.Value, file);
        else await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(_library, "Imported show", 2025, null, null, "123");
        await _ingestion.Received(1).EnsureEpisodeAsync(_library, Arg.Any<SeasonHandle>(), 1, "Pilot", new DateOnly(2025, 6, 1), null, null);
    }

    [Theory]
    [InlineData(LocalMetadataMode.Off)]
    [InlineData(LocalMetadataMode.Prefer)]
    public async Task A_new_season_of_a_show_matched_by_hand_joins_that_show(LocalMetadataMode mode)
    {
        Reads(mode);
        Write("Show/tvshow.nfo", ShowNfo);
        var known = Write("Show/Season 01/Show - S01E01 - First.mkv");
        Write("Show/Season 02/Show - S02E01 - Second.mkv");
        _known.Add(known);
        var matchedByHand = new MediaItemHandle(Guid.NewGuid());
        _ingestion.FindHandMatchedOwnerAsync(_library, known).Returns(matchedByHand);

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureSeasonAsync(_library, matchedByHand, 2);
        await _ingestion.DidNotReceive().EnsureTvShowAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Episodes_loose_in_the_library_folder_never_join_a_show_just_because_one_was_matched_by_hand()
    {
        Reads(LocalMetadataMode.Off);
        var known = Write("Other Show/Other.Show.S01E01.mkv");
        Write("Loose.S01E01.mkv");
        _known.Add(known);
        _ingestion.FindHandMatchedOwnerAsync(_library, known).Returns(new MediaItemHandle(Guid.NewGuid()));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(_library, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }
}
