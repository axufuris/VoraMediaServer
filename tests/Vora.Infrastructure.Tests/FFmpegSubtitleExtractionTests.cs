using Microsoft.Extensions.Logging.Abstractions;
using Vora.Infrastructure.Transcoding;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Extracted subtitles are a cache keyed on (media part, subtitle track), living
// in a subdirectory of the transcode scratch space. The key is content, not
// session, so the same file and track are extracted at most once no matter who
// plays them or how often.
public class FFmpegSubtitleExtractionTests : IDisposable
{
    private static readonly Guid PartId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TrackId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-subs-" + Guid.NewGuid().ToString("N"));

    private static FFmpegSubtitleExtractionService NewService() =>
        new(NullLogger<FFmpegSubtitleExtractionService>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteCached(string content)
    {
        var path = FFmpegSubtitleExtractionService.CachePath(_root, PartId, TrackId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteSource()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "movie.mkv");
        File.WriteAllText(path, "not really a container");
        return path;
    }

    [Fact]
    public void The_cache_lives_in_its_own_subdirectory_of_the_transcode_space()
    {
        var dir = FFmpegSubtitleExtractionService.CacheDirectory("/transcode");

        dir.Should().Be(Path.Combine("/transcode", "subcache"));
    }

    [Fact]
    public void The_cache_file_is_named_for_the_part_and_the_track()
    {
        FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId).Should().Be($"{PartId}_{TrackId}.vtt");
    }

    // Two tracks of the same part, and the same track on two parts, must not
    // share a file — that is the whole point of the key.
    [Fact]
    public void Different_tracks_and_different_parts_get_different_files()
    {
        var a = FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId);
        var b = FFmpegSubtitleExtractionService.WebVttFileName(PartId, Guid.NewGuid());
        var c = FFmpegSubtitleExtractionService.WebVttFileName(Guid.NewGuid(), TrackId);

        new[] { a, b, c }.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void A_written_cache_file_is_usable()
    {
        var path = WriteCached("WEBVTT\n\n00:00.000 --> 00:02.000\nHi\n");

        FFmpegSubtitleExtractionService.IsUsable(path).Should().BeTrue();
    }

    // A zero-byte file is what a killed or crashed ffmpeg leaves behind. Serving
    // it would show an empty subtitle track that never repairs itself, so it has
    // to read as a miss and extract again.
    [Fact]
    public void An_empty_cache_file_is_not_usable()
    {
        var path = WriteCached(string.Empty);

        FFmpegSubtitleExtractionService.IsUsable(path).Should().BeFalse();
    }

    [Fact]
    public void A_missing_cache_file_is_not_usable()
    {
        FFmpegSubtitleExtractionService.IsUsable(Path.Combine(_root, "nope.vtt")).Should().BeFalse();
    }

    // The hit path is the common one: it must answer from disk without going
    // anywhere near ffmpeg. There is no ffmpeg on the test machine, so a run
    // would fail — proving this returned the cached file rather than extracting.
    [Fact]
    public async Task A_cached_track_is_returned_without_extracting()
    {
        var source = WriteSource();
        var cached = WriteCached("WEBVTT\n");

        var result = await NewService().GetOrExtractWebVttAsync(source, 3, 1, _root, PartId, TrackId);

        result.Should().Be(cached);
    }

    [Fact]
    public async Task An_empty_cached_file_is_not_served()
    {
        var source = WriteSource();
        WriteCached(string.Empty);

        var result = await NewService().GetOrExtractWebVttAsync(source, 3, 1, _root, PartId, TrackId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_source_file_yields_nothing()
    {
        var result = await NewService().GetOrExtractWebVttAsync("/no/such/source.mkv", 3, 1, _root, PartId, TrackId);

        result.Should().BeNull();
        Directory.Exists(FFmpegSubtitleExtractionService.CacheDirectory(_root)).Should().BeFalse();
    }

    [Fact]
    public async Task No_configured_transcode_directory_yields_nothing()
    {
        var source = WriteSource();

        (await NewService().GetOrExtractWebVttAsync(source, 3, 1, "", PartId, TrackId)).Should().BeNull();
    }

    // A failed extraction must not leave a zero-byte file at the served name,
    // which would then read as a permanent (empty) cache entry.
    [Fact]
    public async Task A_failed_extraction_leaves_no_cache_entry_behind()
    {
        var source = WriteSource();

        await NewService().GetOrExtractWebVttAsync(source, 3, 1, _root, PartId, TrackId);

        File.Exists(FFmpegSubtitleExtractionService.CachePath(_root, PartId, TrackId)).Should().BeFalse();
        var cacheDir = FFmpegSubtitleExtractionService.CacheDirectory(_root);
        if (Directory.Exists(cacheDir))
        {
            Directory.EnumerateFiles(cacheDir).Should().BeEmpty();
        }
    }

    // ffmpeg is only ever asked for the subtitle stream, so nothing makes it
    // decode video or audio on the way past.
    [Fact]
    public void Video_audio_and_data_are_dropped_from_the_output()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:3", "/transcode/subcache/out.vtt");

        args.Should().ContainInConsecutiveOrder("-vn", "-an", "-dn");
        args.IndexOf("-vn").Should().BeGreaterThan(args.IndexOf("-i"));
        args.IndexOf("-dn").Should().BeLessThan(args.IndexOf("-map"));
    }

    [Fact]
    public void The_webvtt_muxer_is_named_explicitly()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:3", "/transcode/subcache/out.vtt");

        args.Should().ContainInConsecutiveOrder("-c:s", "webvtt");
        args.Should().ContainInConsecutiveOrder("-f", "webvtt");
    }

    [Fact]
    public void The_map_specifier_is_passed_through_verbatim()
    {
        var args = FFmpegSubtitleExtractionService.BuildArguments("/media/movie.mkv", "0:s:1", "/transcode/subcache/out.vtt");

        args.Should().ContainInConsecutiveOrder("-map", "0:s:1");
        args[^1].Should().Be("/transcode/subcache/out.vtt");
    }

    // The two map forms mean different things: the first is the stream's index
    // in the file, the second its position among that file's subtitles.
    [Fact]
    public void The_two_map_forms_are_distinct()
    {
        FFmpegSubtitleExtractionService.AbsoluteMap(3).Should().Be("0:3");
        FFmpegSubtitleExtractionService.SubtitleRelativeMap(1).Should().Be("0:s:1");
    }
}
