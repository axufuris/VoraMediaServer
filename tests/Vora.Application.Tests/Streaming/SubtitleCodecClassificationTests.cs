using Vora.Application.Streaming;

namespace Vora.Application.Tests.Streaming;

// One list decides two things that must never disagree: whether the decision
// manager burns a subtitle into the video, and whether the subtitle endpoint
// will extract it to WebVTT. A codec classed as text in one place and image in
// the other either burns in AND offers a sidecar, or does neither.
public class SubtitleCodecClassificationTests
{
    [Theory]
    [InlineData("pgssub")]
    [InlineData("hdmv_pgs_subtitle")]
    [InlineData("dvd_subtitle")]
    [InlineData("vobsub")]
    public void Image_subtitle_codecs_are_recognised(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeTrue();
    }

    // ffprobe casing varies by container and build, and the decision manager
    // used to lower-case before comparing — the shared helper has to keep doing
    // that or a PGS track would be offered as an extractable text subtitle.
    [Theory]
    [InlineData("PGSSUB")]
    [InlineData("Hdmv_Pgs_Subtitle")]
    [InlineData("  vobsub  ")]
    public void Recognition_ignores_casing_and_surrounding_space(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeTrue();
    }

    [Theory]
    [InlineData("subrip")]
    [InlineData("ass")]
    [InlineData("ssa")]
    [InlineData("mov_text")]
    [InlineData("webvtt")]
    public void Text_subtitle_codecs_are_not_image_subtitles(string codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeFalse();
    }

    // An unanalysed or unknown track is treated as text, which matches the
    // decision manager: it only forces burn-in for codecs it positively knows
    // are bitmaps.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something_new")]
    public void An_unknown_codec_is_not_treated_as_an_image_subtitle(string? codec)
    {
        BestPathDecisionManager.IsImageSubtitleCodec(codec).Should().BeFalse();
    }
}
