using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Subtitles;

// disposition.hearing_impaired is set by almost nothing that muxes a file, so
// the track title is where the fact usually survives. These are the shapes real
// muxers write — and the ones that must NOT be read as a marker, because a false
// positive labels an ordinary subtitle as SDH and pushes it down the auto-pick.
public class SubtitleTraitsTests
{
    [Theory]
    [InlineData("English SDH")]
    [InlineData("eng [SDH]")]
    [InlineData("English (CC)")]
    [InlineData("sdh")]
    [InlineData("English HOH")]
    [InlineData("English - Hearing Impaired")]
    [InlineData("English hearing-impaired")]
    [InlineData("Full.English.SDH")]
    public void A_title_that_says_so_marks_the_track_hearing_impaired(string title)
    {
        SubtitleTraits.TitleSuggestsHearingImpaired(title).Should().BeTrue();
    }

    [Theory]
    [InlineData("English")]
    [InlineData("English Forced")]
    [InlineData("Commentary")]
    [InlineData("Hindi")]
    [InlineData("Soccer match")]
    [InlineData("Hi There")]
    [InlineData("")]
    [InlineData(null)]
    public void An_ordinary_title_is_left_alone(string? title)
    {
        SubtitleTraits.TitleSuggestsHearingImpaired(title).Should().BeFalse();
    }
}
