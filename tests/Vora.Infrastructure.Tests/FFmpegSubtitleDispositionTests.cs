using System.Text.Json;
using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

// What ffprobe says about a subtitle stream is the only description of it Vora
// ever gets. These pin the two facts the player acts on — forced and
// hearing-impaired — including the case the flags leave out, where the muxer
// wrote the fact into the title and nowhere else.
public class FFmpegSubtitleDispositionTests
{
    private static JsonElement Stream(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void A_stream_flagged_hearing_impaired_is_read_as_such()
    {
        var stream = Stream("""
        {
            "index": 3,
            "codec_name": "subrip",
            "disposition": { "default": 0, "forced": 0, "hearing_impaired": 1 },
            "tags": { "language": "eng", "title": "English" }
        }
        """);

        var track = FFmpegAnalyzerService.ReadSubtitleTrack(stream);

        track.IsHearingImpaired.Should().BeTrue();
        track.IsForced.Should().BeFalse();
        track.StreamIndex.Should().Be(3);
        // The tag reader rewrites "eng" as a display name before anything stores
        // it, which is why language matching has to understand names as well as
        // codes.
        track.Language.Should().Be("English");
    }

    [Fact]
    public void An_ordinary_stream_is_neither_forced_nor_hearing_impaired()
    {
        var stream = Stream("""
        {
            "index": 2,
            "codec_name": "subrip",
            "disposition": { "default": 1, "forced": 0, "hearing_impaired": 0 },
            "tags": { "language": "eng", "title": "English" }
        }
        """);

        var track = FFmpegAnalyzerService.ReadSubtitleTrack(stream);

        track.IsHearingImpaired.Should().BeFalse();
        track.IsForced.Should().BeFalse();
        track.IsDefault.Should().BeTrue();
    }

    // The flag is set by almost nothing that muxes a file; the title nearly
    // always survives. Without this fallback most real SDH tracks arrive
    // indistinguishable from the ordinary ones.
    [Fact]
    public void A_title_saying_sdh_stands_in_for_a_flag_nobody_set()
    {
        var stream = Stream("""
        {
            "index": 4,
            "codec_name": "subrip",
            "disposition": { "default": 0, "forced": 0 },
            "tags": { "language": "eng", "title": "English SDH" }
        }
        """);

        FFmpegAnalyzerService.ReadSubtitleTrack(stream).IsHearingImpaired.Should().BeTrue();
    }

    [Fact]
    public void A_forced_stream_keeps_its_forced_flag()
    {
        var stream = Stream("""
        {
            "index": 5,
            "codec_name": "subrip",
            "disposition": { "default": 0, "forced": 1 },
            "tags": { "language": "eng", "title": "English Forced" }
        }
        """);

        var track = FFmpegAnalyzerService.ReadSubtitleTrack(stream);

        track.IsForced.Should().BeTrue();
        track.IsHearingImpaired.Should().BeFalse();
    }

    [Fact]
    public void A_stream_with_no_disposition_block_reports_no_flags()
    {
        var track = FFmpegAnalyzerService.ReadSubtitleTrack(Stream("""{ "index": 1, "codec_name": "ass" }"""));

        track.IsDefault.Should().BeFalse();
        track.IsForced.Should().BeFalse();
        track.IsHearingImpaired.Should().BeFalse();
        track.Language.Should().BeNull();
    }
}
