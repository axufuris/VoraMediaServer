using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Streaming;
using Vora.Infrastructure.Transcoding;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Extracted subtitles are a keep-forever cache keyed on (media part, subtitle
// track), invalidated only when the source actually changes. That invalidation
// is carried by the file NAME — a fingerprint of the source's size, mtime, and
// the track's stream index — so a stale entry is a miss by construction rather
// than something a validity check has to remember to run.
public class FFmpegSubtitleExtractionTests : IDisposable
{
    private static readonly Guid PartId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TrackId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Mtime = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-subs-" + Guid.NewGuid().ToString("N"));

    private static FFmpegSubtitleExtractionService NewService() =>
        new(NullLogger<FFmpegSubtitleExtractionService>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteSource(string content = "not really a container")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "movie.mkv");
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, Mtime);
        return path;
    }

    private string WriteCacheEntry(Guid partId, Guid trackId, string fingerprint, string content = "WEBVTT\n")
    {
        var dir = FFmpegSubtitleExtractionService.CacheDirectory(_root);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FFmpegSubtitleExtractionService.WebVttFileName(partId, trackId, fingerprint));
        File.WriteAllText(path, content);
        return path;
    }

    private string CurrentFingerprint(string sourcePath, int streamIndex) =>
        FFmpegSubtitleExtractionService.FingerprintForSource(sourcePath, streamIndex)!;

    [Fact]
    public void The_cache_lives_in_its_own_subdirectory_of_the_transcode_space()
    {
        FFmpegSubtitleExtractionService.CacheDirectory("/transcode")
            .Should().Be(Path.Combine("/transcode", "subcache"));
    }

    [Fact]
    public void The_cache_file_carries_the_part_the_track_and_the_fingerprint()
    {
        FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId, "deadbeef")
            .Should().Be($"{PartId}_{TrackId}_deadbeef.vtt");
    }

    [Fact]
    public void Different_tracks_and_different_parts_get_different_files()
    {
        var a = FFmpegSubtitleExtractionService.WebVttFileName(PartId, TrackId, "aa");
        var b = FFmpegSubtitleExtractionService.WebVttFileName(PartId, Guid.NewGuid(), "aa");
        var c = FFmpegSubtitleExtractionService.WebVttFileName(Guid.NewGuid(), TrackId, "aa");

        new[] { a, b, c }.Distinct().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(101L, 3)]
    [InlineData(100L, 4)]
    public void A_changed_size_or_stream_index_changes_the_fingerprint(long size, int streamIndex)
    {
        var original = FFmpegSubtitleExtractionService.ComputeFingerprint(100L, Mtime, 3);

        FFmpegSubtitleExtractionService.ComputeFingerprint(size, Mtime, streamIndex).Should().NotBe(original);
    }

    // Re-encoding a file to the exact same byte count is entirely possible, so
    // size alone can't carry the invalidation.
    [Fact]
    public void A_changed_mtime_changes_the_fingerprint_even_at_the_same_size()
    {
        var original = FFmpegSubtitleExtractionService.ComputeFingerprint(100L, Mtime, 3);

        FFmpegSubtitleExtractionService.ComputeFingerprint(100L, Mtime.AddSeconds(1), 3).Should().NotBe(original);
    }

    [Fact]
    public void An_unchanged_source_keeps_the_same_fingerprint()
    {
        var a = FFmpegSubtitleExtractionService.ComputeFingerprint(100L, Mtime, 3);
        var b = FFmpegSubtitleExtractionService.ComputeFingerprint(100L, Mtime, 3);

        a.Should().Be(b);
    }

    [Fact]
    public void A_cache_entry_matching_the_current_source_is_valid()
    {
        var source = WriteSource();
        WriteCacheEntry(PartId, TrackId, CurrentFingerprint(source, 3));

        NewService().HasValidCachedWebVtt(_root, PartId, TrackId, SubtitleSource.Embedded(source, 3, 0)).Should().BeTrue();
    }

    // The heart of the retention rule: nothing ages a cached VTT out, only the
    // source moving underneath it.
    [Fact]
    public void A_cache_entry_from_an_older_version_of_the_file_is_not_valid()
    {
        var source = WriteSource();
        WriteCacheEntry(PartId, TrackId, CurrentFingerprint(source, 3));

        File.WriteAllText(source, "the file was replaced");
        File.SetLastWriteTimeUtc(source, Mtime.AddHours(1));

        NewService().HasValidCachedWebVtt(_root, PartId, TrackId, SubtitleSource.Embedded(source, 3, 0)).Should().BeFalse();
    }

    // A re-probe can move a track to a different stream index; the VTT extracted
    // from the old index is about a different stream now.
    [Fact]
    public void A_cache_entry_for_a_different_stream_index_is_not_valid()
    {
        var source = WriteSource();
        WriteCacheEntry(PartId, TrackId, CurrentFingerprint(source, 3));

        NewService().HasValidCachedWebVtt(_root, PartId, TrackId, SubtitleSource.Embedded(source, 4, 0)).Should().BeFalse();
    }

    // A zero-byte file is what a killed ffmpeg leaves behind. Counting it as a
    // hit would serve an empty subtitle track forever.
    [Fact]
    public void An_empty_cache_entry_is_not_valid()
    {
        var source = WriteSource();
        WriteCacheEntry(PartId, TrackId, CurrentFingerprint(source, 3), content: string.Empty);

        NewService().HasValidCachedWebVtt(_root, PartId, TrackId, SubtitleSource.Embedded(source, 3, 0)).Should().BeFalse();
    }

    [Fact]
    public void A_missing_source_file_is_never_valid()
    {
        NewService().HasValidCachedWebVtt(_root, PartId, TrackId, SubtitleSource.Embedded("/no/such/file.mkv", 3, 0)).Should().BeFalse();
    }

    // The hit path must answer from disk without going near ffmpeg — there is no
    // ffmpeg on the test machine, so a run would fail and return null.
    [Fact]
    public async Task A_valid_cache_entry_is_returned_without_extracting()
    {
        var source = WriteSource();
        var cached = WriteCacheEntry(PartId, TrackId, CurrentFingerprint(source, 3));

        var result = await NewService().GetOrExtractWebVttAsync(SubtitleSource.Embedded(source, 3, 1), _root, PartId, TrackId);

        result.Should().Be(cached);
    }

    [Fact]
    public async Task A_stale_cache_entry_is_treated_as_a_miss()
    {
        var source = WriteSource();
        WriteCacheEntry(PartId, TrackId, "0000000000000000");

        // The miss path runs ffmpeg, which isn't installed here, so it fails —
        // the point is that it did not serve the stale file.
        var result = await NewService().GetOrExtractWebVttAsync(SubtitleSource.Embedded(source, 3, 1), _root, PartId, TrackId);

        result.Should().BeNull();
    }

    [Fact]
    public void Purging_a_part_removes_every_fingerprint_and_track_it_holds()
    {
        var otherPart = Guid.NewGuid();
        WriteCacheEntry(PartId, TrackId, "aaaaaaaaaaaaaaaa");
        WriteCacheEntry(PartId, TrackId, "bbbbbbbbbbbbbbbb");
        WriteCacheEntry(PartId, Guid.NewGuid(), "cccccccccccccccc");
        var survivor = WriteCacheEntry(otherPart, TrackId, "dddddddddddddddd");

        NewService().PurgePart(_root, PartId);

        Directory.EnumerateFiles(FFmpegSubtitleExtractionService.CacheDirectory(_root))
            .Should().BeEquivalentTo(new[] { survivor });
    }

    [Fact]
    public void Purging_a_part_with_nothing_cached_is_not_an_error()
    {
        var act = () => NewService().PurgePart(_root, PartId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Cached_part_ids_are_listed_for_the_orphan_sweep()
    {
        var otherPart = Guid.NewGuid();
        WriteCacheEntry(PartId, TrackId, "aaaaaaaaaaaaaaaa");
        WriteCacheEntry(PartId, Guid.NewGuid(), "bbbbbbbbbbbbbbbb");
        WriteCacheEntry(otherPart, TrackId, "cccccccccccccccc");

        NewService().ListCachedPartIds(_root).Should().BeEquivalentTo(new[] { PartId, otherPart });
    }

    [Fact]
    public void Listing_an_absent_cache_directory_yields_nothing()
    {
        NewService().ListCachedPartIds(_root).Should().BeEmpty();
    }

    // A sidecar is a subtitle file already: nothing to map, nothing to disable.
    [Fact]
    public void Converting_a_sidecar_selects_no_stream()
    {
        var args = FFmpegSubtitleExtractionService.BuildExternalArguments("/media/Movie.en.srt", "/transcode/subcache/out.vtt", null);

        args.Should().NotContain("-map");
        args.Should().ContainInConsecutiveOrder("-i", "/media/Movie.en.srt");
        args.Should().ContainInConsecutiveOrder("-c:s", "webvtt");
        args.Should().ContainInConsecutiveOrder("-f", "webvtt");
        args[^1].Should().Be("/transcode/subcache/out.vtt");
    }

    // ffmpeg assumes UTF-8 and gives up on anything else, which is common in
    // older .srt files. The encoding must precede -i to apply to that input.
    [Fact]
    public void A_character_encoding_is_applied_to_the_input()
    {
        var args = FFmpegSubtitleExtractionService.BuildExternalArguments("/media/Movie.en.srt", "/transcode/subcache/out.vtt", "CP1252");

        args.Should().ContainInConsecutiveOrder("-sub_charenc", "CP1252");
        args.IndexOf("-sub_charenc").Should().BeLessThan(args.IndexOf("-i"));
    }

    // Guessing an encoding for a file that is already UTF-8 corrupts it, so the
    // first attempt names none and lets ffmpeg's own detection run.
    [Fact]
    public void The_first_conversion_attempt_names_no_encoding()
    {
        FFmpegSubtitleExtractionService.BuildExternalArguments("/media/Movie.en.srt", "/out.vtt", null)
            .Should().NotContain("-sub_charenc");
    }

    [Theory]
    [InlineData("/media/Movie.en.vtt", true)]
    [InlineData("/media/Movie.en.VTT", true)]
    [InlineData("/media/Movie.en.srt", false)]
    [InlineData("/media/Movie.en.ass", false)]
    public void Only_a_vtt_sidecar_skips_conversion(string path, bool expected)
    {
        FFmpegSubtitleExtractionService.IsAlreadyWebVtt(path).Should().Be(expected);
    }

    // A sidecar's cues come from the sidecar, so re-saving it must invalidate the
    // cache even though the video it sits beside never changed.
    [Fact]
    public void An_external_source_is_fingerprinted_against_the_sidecar_not_the_video()
    {
        var source = WriteSource();
        Directory.CreateDirectory(_root);
        var sidecar = Path.Combine(_root, "movie.en.srt");
        File.WriteAllText(sidecar, "WEBVTT cues would go here");
        File.SetLastWriteTimeUtc(sidecar, Mtime);

        var external = SubtitleSource.External(source, sidecar);

        external.ContentPath.Should().Be(sidecar);
        external.IsExternal.Should().BeTrue();
        FFmpegSubtitleExtractionService.FingerprintForSource(external.ContentPath, external.StreamIndex)
            .Should().NotBe(FFmpegSubtitleExtractionService.FingerprintForSource(source, -1));
    }

    [Fact]
    public void An_external_source_carries_no_meaningful_stream_index_or_ordinal()
    {
        var external = SubtitleSource.External("/media/movie.mkv", "/media/movie.en.srt");

        external.StreamIndex.Should().BeNegative();
        external.Ordinal.Should().BeNegative();
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
