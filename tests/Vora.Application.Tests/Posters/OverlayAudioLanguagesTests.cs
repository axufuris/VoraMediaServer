using Vora.Application.Posters;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Posters;

// The Multi-Audio tag is about languages, not about how many audio tracks a file
// carries (issue #307).
public class OverlayAudioLanguagesTests
{
    private static MediaAudioTrack Track(string? language, string? title = null, int channels = 6) =>
        new() { Language = language, Title = title, Channels = channels, Codec = "eac3" };

    [Fact]
    public void Two_languages_make_a_title_multi_audio()
    {
        OverlayAudioLanguages.HasMultiple([Track("eng"), Track("jpn")]).Should().BeTrue();
    }

    [Fact]
    public void One_language_in_two_mixes_is_not_multi_audio()
    {
        OverlayAudioLanguages.HasMultiple([Track("eng", channels: 6), Track("eng", channels: 2)]).Should().BeFalse();
    }

    [Fact]
    public void An_English_commentary_beside_the_English_track_is_not_multi_audio()
    {
        OverlayAudioLanguages.HasMultiple([Track("eng"), Track("eng", "Director's commentary", 2)]).Should().BeFalse();
    }

    [Theory]
    [InlineData("eng", "en")]
    [InlineData("eng", "ENG")]
    [InlineData("eng", "en-US")]
    [InlineData("eng", "English")]
    [InlineData("fra", "fre")]
    [InlineData("deu", "ger")]
    public void The_same_language_written_two_ways_counts_once(string first, string second)
    {
        OverlayAudioLanguages.HasMultiple([Track(first), Track(second)]).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("und")]
    [InlineData("UND")]
    [InlineData("zxx")]
    [InlineData("mul")]
    [InlineData("mis")]
    public void A_track_with_no_known_language_counts_for_none(string? unknown)
    {
        OverlayAudioLanguages.HasMultiple([Track("eng"), Track(unknown)]).Should().BeFalse();
        OverlayAudioLanguages.HasMultiple([Track(unknown), Track(unknown)]).Should().BeFalse();
    }

    [Fact]
    public void A_known_language_after_an_unknown_one_still_makes_it_multi_audio()
    {
        OverlayAudioLanguages.HasMultiple([Track(null), Track("eng"), Track("spa")]).Should().BeTrue();
    }

    [Fact]
    public void No_tracks_and_a_single_track_are_not_multi_audio()
    {
        OverlayAudioLanguages.HasMultiple([]).Should().BeFalse();
        OverlayAudioLanguages.HasMultiple([Track("jpn")]).Should().BeFalse();
    }
}
