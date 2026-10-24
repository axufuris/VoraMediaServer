using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class SubtitleRecapAssemblyTests
{
    private readonly MarkerAssembler _assembler = new();

    private static DetectedInterval Black(double startSec, double endSec) => new()
    {
        Start = TimeSpan.FromSeconds(startSec),
        End = TimeSpan.FromSeconds(endSec)
    };

    private List<DetectedMarker> Assemble(double? cueSec, double? fingerprintIntroStartSec, List<DetectedInterval> black, List<TimeSpan>? chapters = null, bool detectIntro = false) =>
        _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(45),
            SilenceIntervals = new List<DetectedInterval>(),
            BlackIntervals = black,
            IsEpisode = true,
            DetectIntro = detectIntro,
            DetectCredits = false,
            ChapterStarts = chapters ?? new List<TimeSpan>(),
            RecapCueStart = cueSec is { } cue ? TimeSpan.FromSeconds(cue) : null,
            KnownIntroStart = fingerprintIntroStartSec is { } intro ? TimeSpan.FromSeconds(intro) : null,
        });

    private static DetectedMarker? Recap(List<DetectedMarker> markers) => markers.SingleOrDefault(m => m.Type == MarkerType.Recap);

    [Fact]
    public void A_previously_on_line_at_the_start_marks_a_recap_to_the_first_long_black_before_the_intro()
    {
        var markers = Assemble(cueSec: 1, fingerprintIntroStartSec: 240, [Black(12, 12.4), Black(71.5, 74), Black(150, 153)]);

        Recap(markers).Should().BeEquivalentTo(new { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(74) });
    }

    [Fact]
    public void Short_cuts_inside_the_recap_do_not_end_it()
    {
        var markers = Assemble(cueSec: 0.5, fingerprintIntroStartSec: 240, [Black(30, 31.5), Black(88, 90.5)]);

        Recap(markers)?.End.Should().Be(TimeSpan.FromSeconds(90.5));
    }

    [Fact]
    public void A_recap_after_a_cold_open_starts_at_the_fade_before_its_first_line()
    {
        var markers = Assemble(cueSec: 64, fingerprintIntroStartSec: 300, [Black(60, 62), Black(140, 143)]);

        Recap(markers).Should().BeEquivalentTo(new { Start = TimeSpan.FromSeconds(62), End = TimeSpan.FromSeconds(143) });
    }

    [Fact]
    public void Without_a_long_black_a_chapter_start_ends_the_recap()
    {
        var markers = Assemble(cueSec: 2, fingerprintIntroStartSec: 240, [Black(40, 41)], [TimeSpan.Zero, TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(240)]);

        Recap(markers)?.End.Should().Be(TimeSpan.FromSeconds(95));
    }

    [Fact]
    public void Nothing_that_could_end_it_means_no_recap_rather_than_a_guess()
    {
        Recap(Assemble(cueSec: 2, fingerprintIntroStartSec: 240, [Black(40, 41)])).Should().BeNull();
    }

    [Fact]
    public void The_recap_never_runs_into_the_intro()
    {
        Recap(Assemble(cueSec: 2, fingerprintIntroStartSec: 60, [Black(70, 73)])).Should().BeNull();
    }

    [Fact]
    public void A_black_too_soon_after_the_line_or_past_two_minutes_is_not_the_end()
    {
        Recap(Assemble(cueSec: 2, fingerprintIntroStartSec: 400, [Black(8, 11)])).Should().BeNull();
        Recap(Assemble(cueSec: 2, fingerprintIntroStartSec: 400, [Black(130, 133)])).Should().BeNull();
    }

    [Fact]
    public void Without_a_recap_line_the_fingerprint_episode_gets_no_recap_from_its_blacks()
    {
        Recap(Assemble(cueSec: null, fingerprintIntroStartSec: 240, [Black(40, 43)])).Should().BeNull();
    }

    [Fact]
    public void The_subtitle_recap_also_bounds_itself_by_a_silence_black_intro()
    {
        var markers = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(45),
            SilenceIntervals = [Black(70, 73), Black(110, 112)],
            BlackIntervals = [Black(70, 73), Black(110, 112)],
            IsEpisode = true,
            DetectIntro = true,
            DetectCredits = false,
            RecapCueStart = TimeSpan.FromSeconds(1),
        });

        markers.Should().ContainSingle(m => m.Type == MarkerType.Intro);
        Recap(markers).Should().BeEquivalentTo(new { Start = TimeSpan.Zero, End = TimeSpan.FromSeconds(73) });
    }
}
