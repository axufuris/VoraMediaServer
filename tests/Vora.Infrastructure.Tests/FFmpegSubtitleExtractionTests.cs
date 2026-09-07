using Microsoft.Extensions.Logging.Abstractions;
using Vora.Infrastructure.Transcoding;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Extracted subtitles are a cache keyed on (media part, subtitle track), living
// in the transcode directory and served by the HLS file route. The file name is
// therefore part of the contract on both sides: it is the cache key, and it is
// what the route's signed-prefix check matches against.
public class FFmpegSubtitleExtractionTests : IDisposable
{
    private static readonly Guid PartId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TrackId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vora-subs-" + Guid.NewGuid().ToString("N"));

    private static FFmpegSubtitleExtractionService NewService() =>
        new(NullLogger<FFmpegSubtitleExtractionService>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string Write(string fileName, string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void The_cache_file_is_named_for_the_part_and_the_track()
    {
        var name = FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId);

        name.Should().Be($"{PartId}_{TrackId}.vtt");
    }

    // Two tracks of the same part, and the same track of two parts, must not
    // share a file — that is the whole point of the key.
    [Fact]
    public void Different_tracks_and_different_parts_get_different_files()
    {
        var a = FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId);
        var b = FFmpegSubtitleExtractionService.WebVttFileName(PartId, Guid.NewGuid());
        var c = FFmpegSubtitleExtractionService.WebVttFileName(Guid.NewGuid(), TrackId);

        new[] { a, b, c }.Distinct().Should().HaveCount(3);
    }

    // The route parses "{guid}_{int}" as a segment request and routes it into the
    // seal-and-wait path. A trailing guid can't parse as an integer, so a
    // subtitle fetch is never mistaken for a segment that never arrives.
    [Fact]
    public void The_cache_file_name_cannot_be_read_as_a_segment_request()
    {
        var name = Path.GetFileNameWithoutExtension(FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId));
        var suffix = name[(name.LastIndexOf('_') + 1)..];

        int.TryParse(suffix, out _).Should().BeFalse();
    }

    // ffmpeg writes here and the result is moved into place only on success, so a
    // half-written file is never at the served name. The staging extension is
    // also outside the route's allowlist, so it can't be fetched even mid-write.
    [Fact]
    public void The_staging_file_is_not_servable()
    {
        var staging = FFmpegSubtitleExtractionService.StagingFileName(PartId, TrackId);

        staging.Should().StartWith(FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId));
        Path.GetExtension(staging).Should().Be(".tmp");
        new[] { ".m3u8", ".ts", ".m4s", ".mp4", ".vtt" }.Should().NotContain(Path.GetExtension(staging));
    }

    [Fact]
    public void A_written_cache_file_is_a_hit()
    {
        Write(FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId), "WEBVTT\n\n00:00.000 --> 00:02.000\nHi\n");

        NewService().TryGetCachedWebVtt(_dir, PartId, TrackId, out var fileName).Should().BeTrue();
        fileName.Should().Be(FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId));
    }

    [Fact]
    public void A_missing_cache_file_is_a_miss()
    {
        NewService().TryGetCachedWebVtt(_dir, PartId, TrackId, out _).Should().BeFalse();
    }

    // A zero-byte file is what a killed or crashed ffmpeg leaves behind. Serving
    // it would show the user an empty subtitle track that never repairs itself,
    // so it has to read as a miss and re-extract.
    [Fact]
    public void An_empty_cache_file_is_a_miss()
    {
        Write(FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId), string.Empty);

        NewService().TryGetCachedWebVtt(_dir, PartId, TrackId, out _).Should().BeFalse();
    }

    [Fact]
    public void A_staged_file_alone_is_not_a_hit()
    {
        Write(FFmpegSubtitleExtractionService.StagingFileName(PartId, TrackId), "WEBVTT");

        NewService().TryGetCachedWebVtt(_dir, PartId, TrackId, out _).Should().BeFalse();
    }

    [Fact]
    public void A_cache_lookup_with_no_directory_configured_is_a_miss()
    {
        NewService().TryGetCachedWebVtt("", PartId, TrackId, out var fileName).Should().BeFalse();
        fileName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_webvtt_muxer_is_named_explicitly()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:3", "/transcode/out.vtt");

        args.Should().ContainInConsecutiveOrder("-f", "webvtt");
        args.Should().ContainInConsecutiveOrder("-c:s", "webvtt");
    }

    [Fact]
    public void The_map_specifier_is_passed_through_verbatim()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:s:1", "/transcode/out.vtt");

        args.Should().ContainInConsecutiveOrder("-map", "0:s:1");
        args[^1].Should().Be("/transcode/out.vtt");
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
            Path.Combine(_dir, "does-not-exist.mkv"), 2, 0, _dir, PartId, TrackId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_source_file_does_not_create_the_cache_directory()
    {
        await NewService().ExtractWebVttAsync("/no/such/source.mkv", 2, 0, _dir, PartId, TrackId);

        Directory.Exists(_dir).Should().BeFalse();
    }

    // Background work is fire-and-forget from the request path, so a failure has
    // to stay inside the returned task rather than surfacing as an unobserved
    // exception on the thread pool.
    [Fact]
    public async Task Background_extraction_of_an_unreadable_source_does_not_throw()
    {
        var act = async () => await NewService().BeginExtractionAsync("/no/such/source.mkv", 2, 0, _dir, PartId, TrackId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Background_extraction_with_no_directory_configured_is_a_no_op()
    {
        await NewService().BeginExtractionAsync("/no/such/source.mkv", 2, 0, "", PartId, TrackId);

        Directory.Exists(_dir).Should().BeFalse();
    }
}
