using Vora.Application.Analysis;
using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Analysis;

public class RecapCuesTests
{
    [Theory]
    [InlineData("Previously on The Terminal List...")]
    [InlineData("<i>Previously on</i> The Terminal List")]
    [InlineData("NARRATOR: Previously on The Boys")]
    [InlineData("[narrator] previously on Severance")]
    [InlineData("PREVIOUSLY ON:")]
    [InlineData("- Previously onFallout")]
    [InlineData("Was bisher geschah")]
    [InlineData("Anteriormente en Breaking Bad")]
    [InlineData("Précédemment dans Lupin")]
    [InlineData("Nelle puntate precedenti")]
    public void A_recap_line_is_recognised(string text)
    {
        RecapCues.IsRecapLine(text).Should().BeTrue();
    }

    [Theory]
    [InlineData("We go in at dawn.")]
    [InlineData("As I said previously, on the radio.")]
    [InlineData("Comme je l'ai dit, précédemment dans la journée")]
    [InlineData("Er war bisher bei uns.")]
    public void Ordinary_dialogue_is_not_a_recap_line(string text)
    {
        RecapCues.IsRecapLine(text).Should().BeFalse();
    }

    [Fact]
    public void The_first_recap_line_inside_the_window_wins()
    {
        var cues = new List<SubtitleCue>
        {
            new(TimeSpan.FromSeconds(140), TimeSpan.FromSeconds(142), "Previously on The Terminal List"),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "We go in at dawn."),
            new(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(11), "Previously on The Terminal List"),
        };

        RecapCues.FirstRecapCueStart(cues, RecapCues.SearchWindow).Should().Be(TimeSpan.FromSeconds(9));
        RecapCues.FirstRecapCueStart(cues, TimeSpan.FromSeconds(5)).Should().BeNull();
    }

    [Fact]
    public void An_english_full_text_track_is_read_first()
    {
        var tracks = new List<SubtitleTrackTargetDto>
        {
            new() { StreamIndex = 2, Codec = "hdmv_pgs_subtitle", Language = "eng" },
            new() { StreamIndex = 3, Codec = "subrip", Language = "spa" },
            new() { StreamIndex = 4, Codec = "subrip", Language = "eng", IsForced = true },
            new() { StreamIndex = 5, Codec = "subrip", Language = "eng" },
            new() { StreamIndex = 6, Codec = "subrip", Language = "en", ExternalFilePath = "/tv/show/ep.en.srt" },
        };

        RecapCues.PickTrack(tracks)?.StreamIndex.Should().Be(5);
    }

    [Fact]
    public void Another_language_is_read_when_there_is_no_english_text_track_and_image_tracks_never()
    {
        RecapCues.PickTrack([new() { StreamIndex = 3, Codec = "ass", Language = "ger" }])?.StreamIndex.Should().Be(3);
        RecapCues.PickTrack([new() { StreamIndex = 2, Codec = "hdmv_pgs_subtitle", Language = "eng" }]).Should().BeNull();
    }
}
