using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class VoraLocalMediaScannerProviderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly IMediaIngestionService _ingestion;
    private readonly VoraLocalMediaScannerProvider _scanner;
    private readonly LibraryHandle _library;

    public VoraLocalMediaScannerProviderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "vora-scanner-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _ingestion = Substitute.For<IMediaIngestionService>();
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());
        _library = LibraryHandle.FromGuid(Guid.NewGuid());

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((new List<string> { _tempRoot }, null, new List<string>())));

        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>())
            .Returns(new HashSet<string>());

        _ingestion.EnsureMovieAsync(
                Arg.Any<LibraryHandle>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));

        _ingestion.EnsureTvShowAsync(
                Arg.Any<LibraryHandle>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));

        _ingestion.EnsureSeasonAsync(Arg.Any<LibraryHandle>(), Arg.Any<MediaItemHandle>(), Arg.Any<int>())
            .Returns(_ => new SeasonHandle(Guid.NewGuid()));

        _ingestion.EnsureEpisodeAsync(
                Arg.Any<LibraryHandle>(),
                Arg.Any<SeasonHandle>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private void TouchFile(string relative)
    {
        var full = Path.Combine(_tempRoot, relative);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(full, "");
    }

    [Fact]
    public async Task ScanMovieLibrary_finds_files_nested_several_directories_deep()
    {
        TouchFile(Path.Combine("A", "B", "C", "Inception (2010).mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "Inception",
            2010,
            tmdbId: null,
            imdbId: null,
            tvdbId: null,
            edition: null);
    }

    // The extension comparison used ToLower(), which is culture-sensitive: in a
    // Turkish locale ".AVI" lowercases to ".avı" and stops matching.
    [Fact]
    public async Task ScanMovieLibrary_matches_an_uppercase_extension()
    {
        TouchFile("Inception (2010).MKV");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "Inception",
            2010,
            tmdbId: null,
            imdbId: null,
            tvdbId: null,
            edition: null);
    }

    [Fact]
    public async Task ScanMovieLibrary_extracts_title_and_year_from_standard_naming()
    {
        TouchFile("Inception (2010).mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "Inception",
            2010,
            tmdbId: null,
            imdbId: null,
            tvdbId: null,
            edition: null);
    }

    [Fact]
    public async Task ScanMovieLibrary_extracts_tmdb_id_when_present()
    {
        TouchFile("The Matrix (1999) {tmdb-603}.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "The Matrix",
            1999,
            tmdbId: "603",
            imdbId: null,
            tvdbId: null,
            edition: null);
    }

    [Fact]
    public async Task ScanMovieLibrary_extracts_imdb_id_when_present()
    {
        TouchFile("Blade Runner (1982) {imdb-tt0083658}.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "Blade Runner",
            1982,
            tmdbId: null,
            imdbId: "tt0083658",
            tvdbId: null,
            edition: null);
    }

    [Fact]
    public async Task ScanMovieLibrary_detects_resolution_from_filename()
    {
        TouchFile("Dune (2021) 2160p.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).AddMediaPartAsync(
            Arg.Any<MediaItemHandle>(),
            Arg.Any<string>(),
            "2160p",
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_detects_edition_tag()
    {
        TouchFile("Blade Runner (1982) Director's Cut.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<string>(),
            1982,
            tmdbId: Arg.Any<string?>(),
            imdbId: Arg.Any<string?>(),
            tvdbId: Arg.Any<string?>(),
            edition: Arg.Is<string?>(e => e != null && e.Contains("Director", StringComparison.OrdinalIgnoreCase)));

        await _ingestion.Received(1).AddMediaPartAsync(
            Arg.Any<MediaItemHandle>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Is<string?>(e => e != null && e.Contains("Director", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_files_matching_exclude_filter()
    {
        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((new List<string> { _tempRoot }, null, new List<string> { ".TDARR" })));

        TouchFile("Inception (2010).mkv");
        TouchFile("Dune (2021) [WEBDL-2160p].TDARR.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(), "Inception", 2010,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
        await _ingestion.DidNotReceive().EnsureMovieAsync(
            Arg.Any<LibraryHandle>(), "Dune", Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_files_inside_an_ignored_folder()
    {
        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((new List<string> { _tempRoot }, null, new List<string> { ".recycle" })));

        TouchFile("Inception (2010).mkv");
        TouchFile(Path.Combine(".recycle", "Dune (2021).mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(), "Inception", 2010,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
        await _ingestion.DidNotReceive().EnsureMovieAsync(
            Arg.Any<LibraryHandle>(), "Dune", Arg.Any<int?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_extracts_edition_from_radarr_edition_tag()
    {
        // "Fan Edit" is not in the keyword list — only the {edition-...} tag yields it.
        TouchFile("Dune (2021) {tmdb-438631} {edition-Fan Edit} [Bluray-2160p].mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "Dune",
            2021,
            tmdbId: "438631",
            imdbId: null,
            tvdbId: null,
            edition: "Fan Edit");
    }

    [Fact]
    public async Task ScanMovieLibrary_falls_back_to_filename_when_no_year()
    {
        TouchFile("WeirdFile.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            "WeirdFile",
            null,
            tmdbId: null,
            imdbId: null,
            tvdbId: null,
            edition: null);
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_unsupported_extensions()
    {
        TouchFile("not-a-movie.txt");
        TouchFile("readme.nfo");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_files_already_in_library()
    {
        TouchFile("Inception (2010).mkv");
        var existingPath = Path.Combine(_tempRoot, "Inception (2010).mkv");

        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>())
            .Returns(new HashSet<string> { existingPath });

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_trailer_and_sample_suffix_files_but_keeps_movie()
    {
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010).mkv"));
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010)-trailer.mkv"));
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010)-sample.mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_skips_files_in_extras_subfolder()
    {
        TouchFile(Path.Combine("Inception (2010)", "Extras", "Making Of.mkv"));
        TouchFile(Path.Combine("Inception (2010)", "Trailers", "Teaser.mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.DidNotReceive().EnsureMovieAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanMovieLibrary_removes_legacy_trailer_items_ingested_as_movies()
    {
        var trailerPath = Path.Combine(_tempRoot, "Inception (2010)", "Inception (2010)-trailer.mkv");
        var moviePath = Path.Combine(_tempRoot, "Inception (2010)", "Inception (2010).mkv");
        _ingestion.GetLibraryItemFilePathsAsync(Arg.Any<LibraryHandle>())
            .Returns(new List<string> { trailerPath, moviePath });

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(trailerPath);
        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(moviePath);
    }

    [Fact]
    public async Task ScanMovieLibrary_attaches_trailer_suffix_as_local_extra_to_parent()
    {
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010).mkv"));
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010)-trailer.mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).AttachLocalExtraAsync(
            Arg.Any<LibraryHandle>(),
            "Inception",
            2010,
            Arg.Is<string>(p => p.EndsWith("-trailer.mkv")),
            "Trailer",
            Arg.Any<string>());
    }

    [Fact]
    public async Task ScanMovieLibrary_attaches_extras_folder_file_to_parent_with_filename_title()
    {
        TouchFile(Path.Combine("Inception (2010)", "Inception (2010).mkv"));
        TouchFile(Path.Combine("Inception (2010)", "Trailers", "Teaser One.mkv"));

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).AttachLocalExtraAsync(
            Arg.Any<LibraryHandle>(),
            "Inception",
            2010,
            Arg.Is<string>(p => p.EndsWith("Teaser One.mkv")),
            "Trailer",
            "Teaser One");
    }

    [Fact]
    public async Task ScanTvShowLibrary_extracts_season_episode_from_filename()
    {
        TouchFile(Path.Combine("Breaking Bad (2008)", "Season 01", "S01E03 - And the Bag's in the River.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(
            Arg.Any<LibraryHandle>(),
            "Breaking Bad",
            2008,
            tmdbId: null,
            imdbId: null,
            tvdbId: null);

        await _ingestion.Received(1).EnsureSeasonAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<MediaItemHandle>(),
            1);

        await _ingestion.Received(1).EnsureEpisodeAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<SeasonHandle>(),
            3,
            "And the Bag's in the River",
            Arg.Any<DateOnly?>(),
            Arg.Any<string?>());
    }

    [Theory]
    [InlineData("Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol [HDTV-720p][AAC 2.0][x264]-NGP.mkv", 9, 10, "Its a Wonderful Christmas Carol")]
    [InlineData("Red vs. Blue (2003) - S01E01-E19 - Why Are We Here [Bluray-1080p][AC3 5.1][x264]-none.mkv", 1, 19, "Why Are We Here")]
    [InlineData("Lost (2004) - S06E17 - E18 - The End.mkv", 17, 18, "The End")]
    [InlineData("Lost (2004) - S06E17E18 - The End.mkv", 17, 18, "The End")]
    [InlineData("Red vs. Blue (2003) - S02E01-E02-E03 - Everything Old Is New Again.mkv", 1, 3, "Everything Old Is New Again")]
    public async Task ScanTvShowLibrary_records_the_episode_range_of_a_multi_episode_file(string fileName, int first, int last, string title)
    {
        TouchFile(Path.Combine("Some Show (2020)", "Season 01", fileName));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureEpisodeAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<SeasonHandle>(),
            first,
            title,
            Arg.Any<DateOnly?>(),
            Arg.Any<string?>(),
            last);
    }

    [Fact]
    public async Task ScanTvShowLibrary_still_reads_an_absolute_number_as_part_of_the_name_not_the_title()
    {
        TouchFile(Path.Combine("One Piece (1999)", "Season 01", "One Piece (1999) - S01E01 - 001 - I'm Luffy.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureEpisodeAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<SeasonHandle>(),
            1,
            "I'm Luffy",
            Arg.Any<DateOnly?>(),
            Arg.Any<string?>(),
            null);
    }

    [Fact]
    public async Task ScanTvShowLibrary_treats_the_pattern_older_libraries_saved_as_the_current_default()
    {
        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>((new List<string> { _tempRoot }, RetiredTvPattern, new List<string>())));
        TouchFile(Path.Combine("Ghosts (US) (2021)", "Season 05", "Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureEpisodeAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<SeasonHandle>(),
            9,
            "Its a Wonderful Christmas Carol",
            Arg.Any<DateOnly?>(),
            Arg.Any<string?>(),
            10);
    }

    [Fact]
    public async Task ScanTvShowLibrary_records_ranges_for_files_it_already_knows()
    {
        var doubleEpisode = Path.Combine(_tempRoot, "Ghosts (US) (2021)", "Season 05", "Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol.mkv");
        var singleEpisode = Path.Combine(_tempRoot, "Ghosts (US) (2021)", "Season 05", "Ghosts (US) (2021) - S05E11 - The Christmas Spirit.mkv");
        var extra = Path.Combine(_tempRoot, "Ghosts (US) (2021)", "Season 05", "Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol-trailer.mkv");
        var elsewhere = Path.Combine(Path.GetTempPath(), "other-library", "Show - S01E01-E02 - Pilot.mkv");
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>())
            .Returns(new HashSet<string> { doubleEpisode, singleEpisode, extra, elsewhere });

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).SetEpisodeRangesAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Is<IReadOnlyDictionary<string, int>>(ranges => ranges.Count == 1 && ranges[doubleEpisode] == 10));
    }

    private const string RetiredTvPattern = @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";

    [Fact]
    public async Task ScanTvShowLibrary_extracts_air_date_episode_naming()
    {
        TouchFile(Path.Combine("Daily Show (1999)", "2024-01-15 - Some Episode.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureEpisodeAsync(
            Arg.Any<LibraryHandle>(),
            Arg.Any<SeasonHandle>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            new DateOnly(2024, 1, 15),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ScanTvShowLibrary_attaches_show_level_extra_to_show()
    {
        TouchFile(Path.Combine("Breaking Bad (2008)", "Season 01", "S01E01 - Pilot.mkv"));
        TouchFile(Path.Combine("Breaking Bad (2008)", "Extras", "Making Of.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).AttachTvShowLocalExtraAsync(
            Arg.Any<LibraryHandle>(),
            "Breaking Bad",
            Arg.Is<string>(p => p.EndsWith("Making Of.mkv")),
            Arg.Any<string>(),
            "Making Of");
    }

    [Fact]
    public async Task ScanTvShowLibrary_attaches_season_folder_extra_to_show()
    {
        TouchFile(Path.Combine("Breaking Bad (2008)", "Season 01", "S01E01 - Pilot.mkv"));
        TouchFile(Path.Combine("Breaking Bad (2008)", "Season 01", "S01E01 - Pilot-trailer.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).AttachTvShowLocalExtraAsync(
            Arg.Any<LibraryHandle>(),
            "Breaking Bad",
            Arg.Is<string>(p => p.EndsWith("-trailer.mkv")),
            "Trailer",
            Arg.Any<string>());
    }

    [Fact]
    public async Task ScanTvShowLibrary_extracts_show_provider_id_from_folder_bracket()
    {
        TouchFile(Path.Combine("Dark (2017) [tmdb-70523]", "Season 01", "S01E01 - Secrets.mkv"));

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(
            Arg.Any<LibraryHandle>(),
            "Dark",
            2017,
            tmdbId: "70523",
            imdbId: null,
            tvdbId: null);
    }
}
