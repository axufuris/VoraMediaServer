using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class HomeVideoScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-home-video-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaIngestionService _ingestion = Substitute.For<IMediaIngestionService>();
    private readonly LibraryHandle _library = LibraryHandle.FromGuid(Guid.NewGuid());
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly VoraLocalMediaScannerProvider _scanner;
    private List<string> _excluded = [];

    public HomeVideoScanTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(_ => Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>(([_root], null, _excluded)));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(_known);
        _ingestion.AddHomeVideoAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<DateOnly?>())
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

    [Theory]
    [InlineData("IMG_20190704_123456", 2019, 7, 4)]
    [InlineData("VID-20210612-WA0001", 2021, 6, 12)]
    [InlineData("PXL_20230101_123456789", 2023, 1, 1)]
    [InlineData("2020-03-01 Birthday", 2020, 3, 1)]
    [InlineData("Beach 2019.07.04", 2019, 7, 4)]
    [InlineData("Screen Recording 2024_02_29 at 10.00.00", 2024, 2, 29)]
    public void A_date_in_the_name_is_the_videos_date(string name, int year, int month, int day)
    {
        HomeVideoNames.DateInName(name).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("IMG_1234")]
    [InlineData("Party 20190231")]
    [InlineData("Trip 2019-0704")]
    [InlineData("Christmas 1080p")]
    [InlineData("Clip 120190704")]
    public void A_name_without_a_whole_date_gives_none(string name)
    {
        HomeVideoNames.DateInName(name).Should().BeNull();
    }

    [Fact]
    public void A_video_without_a_date_in_its_name_takes_the_files_date()
    {
        var file = Write("Holiday/Opening presents.mp4");
        File.SetLastWriteTimeUtc(file, new DateTime(2018, 12, 25, 9, 30, 0, DateTimeKind.Utc));

        HomeVideoNames.Date(file).Should().Be(new DateOnly(2018, 12, 25));
        HomeVideoNames.Title(file).Should().Be("Opening presents");
    }

    [Fact]
    public async Task Every_file_is_its_own_video_even_with_the_same_name()
    {
        var summer = Write("Summer/IMG_0001.mp4");
        var winter = Write("Winter/IMG_0001.mp4");

        await _scanner.ScanHomeVideoLibraryAsync(_library.Value);

        await _ingestion.Received(2).AddHomeVideoAsync(_library, "IMG_0001", Arg.Any<DateOnly?>());
        await _ingestion.Received(1).AddMediaPartAsync(Arg.Any<MediaItemHandle>(), Arg.Is(summer), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>());
        await _ingestion.Received(1).AddMediaPartAsync(Arg.Any<MediaItemHandle>(), Arg.Is(winter), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>());
        await _ingestion.DidNotReceive().EnsureMovieAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task A_video_is_titled_from_its_name_and_dated_from_it()
    {
        Write("2019-07-04 Beach Day 1080p.mov");

        await _scanner.ScanHomeVideoLibraryAsync(_library.Value);

        await _ingestion.Received(1).AddHomeVideoAsync(_library, "2019-07-04 Beach Day 1080p", new DateOnly(2019, 7, 4));
        await _ingestion.Received(1).AddMediaPartAsync(Arg.Any<MediaItemHandle>(), Arg.Any<string>(), Arg.Is<string?>("1080p"), Arg.Any<string?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Known_excluded_and_non_video_files_are_left_out()
    {
        _known.Add(Write("Old/IMG_0001.mp4"));
        Write("Phone/.thumbnails/IMG_0002.mp4");
        Write("Phone/IMG_0002.jpg");
        Write("Phone/IMG_0003.mp4");
        _excluded = [".thumbnails"];

        await _scanner.ScanHomeVideoLibraryAsync(_library.Value);

        await _ingestion.Received(1).AddHomeVideoAsync(_library, Arg.Any<string>(), Arg.Any<DateOnly?>());
        await _ingestion.Received(1).AddHomeVideoAsync(_library, "IMG_0003", Arg.Any<DateOnly?>());
    }

    [Fact]
    public async Task The_folder_watcher_adds_one_new_video_and_skips_one_it_has()
    {
        var known = Write("Phone/IMG_0001.mp4");
        _known.Add(known);
        var added = Write("Phone/IMG_0002.mp4");

        (await _scanner.ScanHomeVideoFileAsync(_library.Value, known)).Should().BeNull();
        (await _scanner.ScanHomeVideoFileAsync(_library.Value, added)).Should().NotBeNull();

        await _ingestion.Received(1).AddHomeVideoAsync(_library, "IMG_0002", Arg.Any<DateOnly?>());
    }
}
