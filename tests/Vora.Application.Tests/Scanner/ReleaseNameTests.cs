using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class ReleaseNameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-release-names-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaIngestionService _ingestion = Substitute.For<IMediaIngestionService>();
    private readonly LibraryHandle _library = LibraryHandle.FromGuid(Guid.NewGuid());
    private readonly VoraLocalMediaScannerProvider _scanner;

    public ReleaseNameTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>(([_root], null, [])));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(_ => new HashSet<string>());
        _ingestion.GetFirstEpisodeFilePathsAsync(Arg.Any<LibraryHandle>()).Returns([]);
        _ingestion.GetFilesOfMoviesWithSeveralFilesAsync(Arg.Any<LibraryHandle>()).Returns([]);
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

    private void Write(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _root);
        File.WriteAllText(path, string.Empty);
    }

    [Theory]
    [InlineData("Lee.2024.1080p", "Lee", 2024)]
    [InlineData("The.Mummy.2004.1080p", "The Mummy", 2004)]
    [InlineData("Lee_2024_1080p_WEB-DL", "Lee", 2024)]
    [InlineData("Blade.Runner.2049.2017.2160p.UHD.BluRay.x265-GROUP", "Blade Runner 2049", 2017)]
    [InlineData("2001.A.Space.Odyssey.1968.1080p", "2001 A Space Odyssey", 1968)]
    [InlineData("Apollo.13.1995.1080p", "Apollo 13", 1995)]
    [InlineData("1917.2019.2160p", "1917", 2019)]
    [InlineData("Lee.(2024).1080p", "Lee", 2024)]
    [InlineData("S.W.A.T.2003.1080p", "S.W.A.T.", 2003)]
    [InlineData("A.I.Artificial.Intelligence.2001", "A.I. Artificial Intelligence", 2001)]
    [InlineData("The.A.Team.2010", "The A Team", 2010)]
    [InlineData("Heat 1995 1080p", "Heat", 1995)]
    [InlineData("Mr.Robot.S01.1080p.WEB-DL", "Mr Robot", null)]
    [InlineData("Some.Movie.1080p.BluRay", "Some Movie", null)]
    public void A_release_name_gives_its_title_and_year(string name, string title, int? year)
    {
        ReleaseNames.Read(name).Should().Be((title, year));
    }

    [Theory]
    [InlineData("Wonder Woman 1984")]
    [InlineData("Death Race 2000")]
    [InlineData("Mr. & Mrs. Smith")]
    [InlineData("A.C.O.D.")]
    [InlineData("R.I.P.D.")]
    [InlineData("Slasher.com")]
    [InlineData("Waiting...")]
    [InlineData("1917")]
    [InlineData("")]
    public void A_name_that_is_not_a_release_name_is_left_alone(string name)
    {
        ReleaseNames.Read(name).Should().BeNull();
    }

    [Fact]
    public async Task A_film_with_a_dotted_release_name_is_read_as_its_title_and_year()
    {
        Write("Lee.2024.1080p.WEB-DL.x264-GROUP.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Lee", 2024, null, null, null, null);
    }

    [Fact]
    public async Task A_film_named_badly_in_a_dotted_release_folder_takes_the_folders_title_and_year()
    {
        Write("The.Mummy.2004.1080p.BluRay/movie.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "The Mummy", 2004, null, null, null, null);
    }

    [Fact]
    public async Task A_title_ending_in_a_number_is_not_cut_at_it()
    {
        Write("Wonder Woman 1984.mkv");

        await _scanner.ScanMovieLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureMovieAsync(_library, "Wonder Woman 1984", null, null, null, null, null);
    }

    [Fact]
    public async Task A_show_in_a_dotted_release_folder_is_read_as_its_title_and_year()
    {
        Write("Severance.2022.1080p.WEB-DL/Season 1/Severance.S01E01.mkv");

        await _scanner.ScanTvShowLibraryAsync(_library.Value);

        await _ingestion.Received(1).EnsureTvShowAsync(_library, "Severance", 2022, null, null, null);
    }
}
