using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class MovieIdentityScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-movie-identity-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaIngestionService _ingestion = Substitute.For<IMediaIngestionService>();
    private readonly LibraryHandle _library = LibraryHandle.FromGuid(Guid.NewGuid());
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly VoraLocalMediaScannerProvider _scanner;
    private List<string> _libraryFolders;

    public MovieIdentityScanTests()
    {
        Directory.CreateDirectory(_root);
        _libraryFolders = [_root];
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(_ => Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((_libraryFolders, null, [])));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(_known);
        _ingestion.GetFilesOfMoviesWithSeveralFilesAsync(Arg.Any<LibraryHandle>()).Returns([]);
        _ingestion.EnsureMovieAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _root);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private Task Added(string title, int? year, string? tmdbId = null, string? imdbId = null) =>
        _ingestion.Received(1).EnsureMovieAsync(_library, title, year, tmdbId, imdbId, null, null);

    [Fact]
    public async Task A_file_without_a_year_takes_the_year_of_its_movie_folder()
    {
        Write("Heat (1995)/Heat.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", 1995);
    }

    [Fact]
    public async Task A_scene_named_file_alone_in_its_folder_takes_the_folders_title_and_year()
    {
        Write("Heat (1995)/heat.1995.1080p.bluray.x264-group.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", 1995);
    }

    [Fact]
    public async Task Ids_in_the_folder_name_fill_the_ids_the_file_name_leaves_out()
    {
        Write("Heat (1995) {imdb-tt0113277}/Heat.mkv");
        Write("Alien (1979) [tmdbid-348]/Alien {tmdb-999}.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", 1995, imdbId: "tt0113277");
        await Added("Alien", 1979, tmdbId: "999");
    }

    [Fact]
    public async Task A_folder_holding_several_films_lends_its_year_only_to_the_one_it_is_named_after()
    {
        Write("Heat (1995)/Heat.mkv");
        Write("Heat (1995)/Heat Bonus Disc.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", 1995);
        await Added("Heat Bonus Disc", null);
    }

    [Fact]
    public async Task A_year_in_the_file_name_wins_over_the_folders()
    {
        Write("Heat (1995)/Heat (1986).mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", 1986);
    }

    [Fact]
    public async Task A_file_straight_in_a_library_folder_never_takes_that_folders_name()
    {
        var library = Path.Combine(_root, "Movies (2024)");
        _libraryFolders = [library];
        Write("Movies (2024)/Heat.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await Added("Heat", null);
    }

    private string Known(string relative)
    {
        var path = Write(relative);
        _known.Add(path);
        return path;
    }

    private void Placed(Guid movieId, string? imdbId, bool matchedByHand, params string[] files) =>
        _ingestion.GetFilesOfMoviesWithSeveralFilesAsync(Arg.Any<LibraryHandle>())
            .Returns(files.Select(file => new MovieFilePlacement(file, movieId, null, imdbId, matchedByHand)).ToList());

    [Fact]
    public async Task A_file_of_another_film_stacked_on_one_is_taken_off_and_added_as_its_own_film()
    {
        var hd = Known("1080p/The Princess (2022)/The Princess (2022) {imdb-tt13406136} [WEBDL-1080p].mkv");
        var uhd = Known("4K/The Princess (2022)/The Princess (2022) {imdb-tt13393128} [WEBDL-2160p].mkv");
        Placed(Guid.NewGuid(), "tt13406136", false, hd, uhd);

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(uhd);
        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(hd);
        await Added("The Princess", 2022, imdbId: "tt13393128");
    }

    [Fact]
    public async Task The_library_scan_takes_the_file_off_before_it_lists_new_files()
    {
        var hd = Known("1080p/The Princess (2022)/The Princess (2022) {imdb-tt13406136}.mkv");
        var uhd = Known("4K/The Princess (2022)/The Princess (2022) {imdb-tt13393128}.mkv");
        Placed(Guid.NewGuid(), "tt13406136", false, hd, uhd);

        var units = await _scanner.DiscoverMovieScanUnitsAsync(_library.Value);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(uhd);
        units.SelectMany(u => u.FilePaths).Should().Equal(uhd);
    }

    [Fact]
    public async Task A_film_matched_by_hand_keeps_every_file()
    {
        var hd = Known("1080p/The Princess (2022)/The Princess (2022) {imdb-tt13406136}.mkv");
        var uhd = Known("4K/The Princess (2022)/The Princess (2022) {imdb-tt13393128}.mkv");
        Placed(Guid.NewGuid(), "tt13406136", true, hd, uhd);

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Files_without_ids_and_files_sharing_the_films_ids_stay()
    {
        var tagged = Known("1080p/Heat (1995)/Heat (1995) {imdb-tt0113277}.mkv");
        var plain = Known("4K/Heat (1995)/Heat (1995).mkv");
        Placed(Guid.NewGuid(), "tt0113277", false, tagged, plain);

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task When_no_file_shares_the_films_ids_nothing_is_moved()
    {
        var one = Known("1080p/Role Play (2023)/Role Play (2023) {imdb-tt14997890}.mkv");
        var two = Known("4K/Role Play (2023)/Role Play (2023) {imdb-tt14997890}.mkv");
        Placed(Guid.NewGuid(), "tt12964320", false, one, two);

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(Arg.Any<string>());
    }
}
