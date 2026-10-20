using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Analysis;

public class PictureAreaManagerTests
{
    private static readonly Guid Library = Guid.NewGuid();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IPictureAreaDetector _detector = Substitute.For<IPictureAreaDetector>();

    private PictureAreaManager Manager() => new(_media, _detector, new NullTaskProgressReporter(), NullLogger<PictureAreaManager>.Instance);

    private static PictureAreaTarget Target(string file) => new(Guid.NewGuid(), Guid.NewGuid(), file, 0, 1920, 1080, 2750);

    [Fact]
    public async Task Each_video_of_a_library_that_was_not_checked_is_saved_with_what_was_found()
    {
        var letterboxed = Target("/tv/Lioness/S02E05.mkv");
        var fullFrame = Target("/tv/Lioness/S02E06.mkv");
        _media.GetPictureAreaTargetsForLibraryAsync(Library).Returns([letterboxed, fullFrame]);
        _detector.DetectAsync(letterboxed, Arg.Any<CancellationToken>()).Returns(new PictureCrop(60, 0, 60, 0));
        _detector.DetectAsync(fullFrame, Arg.Any<CancellationToken>()).Returns(PictureCrop.None);

        await Manager().DetectForLibraryAsync(Library, TestContext.Current.CancellationToken);

        await _media.Received(1).SavePictureCropAsync(letterboxed.VideoTrackId, new PictureCrop(60, 0, 60, 0));
        await _media.Received(1).SavePictureCropAsync(fullFrame.VideoTrackId, PictureCrop.None);
    }

    [Fact]
    public async Task A_video_ffmpeg_could_not_read_is_left_for_the_next_pass()
    {
        var unreadable = Target("/movies/Broken.mkv");
        var fine = Target("/movies/Fine.mkv");
        _media.GetPictureAreaTargetsForItemAsync(Arg.Any<Guid>()).Returns([unreadable, fine]);
        _detector.DetectAsync(unreadable, Arg.Any<CancellationToken>()).Returns((PictureCrop?)null);
        _detector.DetectAsync(fine, Arg.Any<CancellationToken>()).Returns(PictureCrop.None);

        await Manager().DetectForItemAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await _media.DidNotReceive().SavePictureCropAsync(unreadable.VideoTrackId, Arg.Any<PictureCrop>());
        await _media.Received(1).SavePictureCropAsync(fine.VideoTrackId, PictureCrop.None);
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_pass()
    {
        var throwing = Target("/movies/Locked.mkv");
        var fine = Target("/movies/Fine.mkv");
        _media.GetPictureAreaTargetsForLibraryAsync(Library).Returns([throwing, fine]);
        _detector.DetectAsync(throwing, Arg.Any<CancellationToken>()).Returns<PictureCrop?>(_ => throw new IOException("The file is in use."));
        _detector.DetectAsync(fine, Arg.Any<CancellationToken>()).Returns(new PictureCrop(0, 240, 0, 240));

        await Manager().DetectForLibraryAsync(Library, TestContext.Current.CancellationToken);

        await _media.Received(1).SavePictureCropAsync(fine.VideoTrackId, new PictureCrop(0, 240, 0, 240));
    }
}
