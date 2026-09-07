using Microsoft.Extensions.Logging.Abstractions;
using Vora.Infrastructure.Transcoding;
using Xunit;

namespace Vora.Infrastructure.Tests;

// The sidecar lands in the same flat transcode directory as the playlist and
// segments, and the HLS file route only serves a name that starts with the
// prefix its token signs — the transcode key. So the file name is part of the
// contract, not an implementation detail.
public class FFmpegSubtitleExtractionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vora-subs-" + Guid.NewGuid().ToString("N"));

    private static FFmpegSubtitleExtractionService NewService() =>
        new(NullLogger<FFmpegSubtitleExtractionService>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_file_name_is_prefixed_with_the_transcode_key()
    {
        var key = Guid.NewGuid();

        var name = FFmpegSubtitleExtractionService.WebVttFileName(key);

        name.Should().StartWith(key.ToString());
        name.Should().EndWith(".vtt");
    }

    // The route parses "{guid}_{int}" as a segment request and waits for FFmpeg
    // to seal it. The sidecar shares that underscore shape, so its suffix has to
    // stay non-numeric or the player's subtitle fetch would be treated as a
    // segment that never arrives.
    [Fact]
    public void The_file_name_cannot_be_read_as_a_segment_request()
    {
        var name = Path.GetFileNameWithoutExtension(FFmpegSubtitleExtractionService.WebVttFileName(Guid.NewGuid()));
        var suffix = name[(name.LastIndexOf('_') + 1)..];

        int.TryParse(suffix, out _).Should().BeFalse();
    }

    // Without an explicit -f, ffmpeg picks the muxer from the output extension.
    // .vtt resolves, but only by inference — naming the standalone WebVTT muxer
    // removes the guess.
    [Fact]
    public void The_webvtt_muxer_is_named_explicitly()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:3", "/transcode/out.vtt");

        args.Should().ContainInConsecutiveOrder("-f", "webvtt");
        args.Should().ContainInConsecutiveOrder("-c:s", "webvtt");
    }

    [Fact]
    public void The_output_path_is_the_last_argument()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:3", "/transcode/out.vtt");

        args[^1].Should().Be("/transcode/out.vtt");
    }

    [Fact]
    public void The_map_specifier_is_passed_through_verbatim()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:s:1", "/transcode/out.vtt");

        args.Should().ContainInConsecutiveOrder("-map", "0:s:1");
    }

    // The two map forms mean different things: the first is the stream's index
    // in the file, the second its position among that file's subtitles.
    [Fact]
    public void The_two_map_forms_are_distinct()
    {
        FFmpegSubtitleExtractionService.AbsoluteMap(3).Should().Be("0:3");
        FFmpegSubtitleExtractionService.SubtitleRelativeMap(1).Should().Be("0:s:1");
    }

    [Fact]
    public async Task A_missing_source_file_yields_no_sidecar()
    {
        var result = await NewService().ExtractWebVttAsync(
            Path.Combine(_dir, "does-not-exist.mkv"), 2, 0, _dir, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_source_file_does_not_create_the_output_directory()
    {
        await NewService().ExtractWebVttAsync("/no/such/source.mkv", 2, 0, _dir, Guid.NewGuid());

        Directory.Exists(_dir).Should().BeFalse();
    }

    [Fact]
    public void Removing_the_sidecar_deletes_it()
    {
        var key = Guid.NewGuid();
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, FFmpegSubtitleExtractionService.WebVttFileName(key));
        File.WriteAllText(path, "WEBVTT");

        NewService().RemoveWebVtt(_dir, key);

        File.Exists(path).Should().BeFalse();
    }

    // Called on every session that wants no subtitles, so the common case is
    // that there is nothing there.
    [Fact]
    public void Removing_a_sidecar_that_was_never_written_is_not_an_error()
    {
        var act = () => NewService().RemoveWebVtt(_dir, Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void Removing_only_touches_the_key_it_was_given()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        Directory.CreateDirectory(_dir);
        var otherPath = Path.Combine(_dir, FFmpegSubtitleExtractionService.WebVttFileName(theirs));
        File.WriteAllText(Path.Combine(_dir, FFmpegSubtitleExtractionService.WebVttFileName(mine)), "WEBVTT");
        File.WriteAllText(otherPath, "WEBVTT");

        NewService().RemoveWebVtt(_dir, mine);

        File.Exists(otherPath).Should().BeTrue();
    }
}
