using Vora.Application.Streaming;
using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Streaming;

// The web client carries its own copy of this list — it has to decide whether
// picking a subtitle restarts the stream before it asks the server anything. The
// two must agree, and for a while they did not: the client classed dvb_subtitle,
// xsub and bare pgs as bitmaps while the server called them text. The result was
// the worst of both: the client asked for a burn-in the server would not do, the
// server offered a sidecar it could never produce, and the viewer got a stream
// restart and no subtitle.
public class SubtitleCodecParityTests
{
    // Mirrors IMAGE_SUBTITLE_CODECS in Vora.Web/src/utils/subtitleKind.ts.
    private static readonly string[] ClientImageCodecs =
    [
        "hdmv_pgs_subtitle",
        "pgssub",
        "pgs",
        "dvd_subtitle",
        "vobsub",
        "dvb_subtitle",
        "dvbsub",
        "xsub",
    ];

    [Theory]
    [MemberData(nameof(ClientCodecs))]
    public void Every_codec_the_client_burns_in_is_a_bitmap_to_the_server(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeTrue();
    }

    // The other direction matters just as much: a codec the server burns in but
    // the client thinks is text would render the subtitle twice.
    [Theory]
    [MemberData(nameof(ClientCodecs))]
    public void And_is_therefore_never_offered_for_extraction(string codec)
    {
        SubtitlePreExtractionManager.IsExtractableSubtitleCodec(codec).Should().BeFalse();
    }

    // Broadcast-sourced recordings are where the missing codecs actually turn up,
    // which is exactly the material a DVR produces.
    [Theory]
    [InlineData("dvb_subtitle")]
    [InlineData("dvbsub")]
    [InlineData("xsub")]
    [InlineData("pgs")]
    public void The_codecs_that_used_to_be_missed_are_recognised(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeTrue();
    }

    [Theory]
    [InlineData("subrip")]
    [InlineData("ass")]
    [InlineData("ssa")]
    [InlineData("mov_text")]
    [InlineData("webvtt")]
    public void Text_codecs_stay_extractable_on_both_sides(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeFalse();
        SubtitlePreExtractionManager.IsExtractableSubtitleCodec(codec).Should().BeTrue();
    }

    public static TheoryData<string> ClientCodecs()
    {
        var data = new TheoryData<string>();
        foreach (var codec in ClientImageCodecs) data.Add(codec);
        return data;
    }
}
