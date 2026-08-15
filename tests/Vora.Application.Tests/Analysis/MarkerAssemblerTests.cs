using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class MarkerAssemblerTests
{
    private readonly MarkerAssembler _assembler = new();

    private static DetectedInterval Interval(double startSec, double endSec) => new()
    {
        Start = TimeSpan.FromSeconds(startSec),
        End = TimeSpan.FromSeconds(endSec)
    };

    [Fact]
    public void Returns_no_markers_when_duration_is_zero()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.Zero,
            SilenceIntervals = new List<DetectedInterval>(),
            BlackIntervals = new List<DetectedInterval>()
        });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Returns_no_markers_when_silence_and_black_have_no_joint_gaps()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(30),
            // silence and black at non-overlapping ranges
            SilenceIntervals = new List<DetectedInterval> { Interval(10, 20) },
            BlackIntervals = new List<DetectedInterval> { Interval(100, 110) }
        });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detects_intro_from_a_joint_silence_black_gap_in_first_8_minutes()
    {
        // Joint gap at 30s-90s (silence + black overlap) → intro 0s..90s
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(45),
            SilenceIntervals = new List<DetectedInterval> { Interval(30, 90) },
            BlackIntervals = new List<DetectedInterval> { Interval(30, 90) }
        });

        var intro = result.Single(m => m.Type == MarkerType.Intro);
        intro.Start.Should().Be(TimeSpan.Zero);
        intro.End.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Skips_intro_when_first_gap_is_outside_8_minute_window()
    {
        // First joint gap starts at 10 minutes — outside the 8-minute intro window
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(60),
            SilenceIntervals = new List<DetectedInterval> { Interval(600, 660) },
            BlackIntervals = new List<DetectedInterval> { Interval(600, 660) }
        });

        result.Where(m => m.Type == MarkerType.Intro).Should().BeEmpty();
    }

    [Fact]
    public void Detects_recap_before_intro_for_episodes()
    {
        // Episode with a "previously on" segment ending at ~40s and a theme/title
        // section ending at ~120s should produce TWO nested markers:
        //   Recap = 0..40s  ("Skip Recap" button)
        //   Intro = 0..120s ("Skip Intro" button covers both)
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(40),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval> { Interval(30, 40), Interval(60, 120) },
            BlackIntervals = new List<DetectedInterval> { Interval(30, 40), Interval(60, 120) }
        });

        var recap = result.Single(m => m.Type == MarkerType.Recap);
        recap.Start.Should().Be(TimeSpan.Zero);
        recap.End.Should().Be(TimeSpan.FromSeconds(40));

        var intro = result.Single(m => m.Type == MarkerType.Intro);
        intro.Start.Should().Be(TimeSpan.Zero);
        intro.End.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Single_gap_in_intro_window_produces_intro_but_no_recap()
    {
        // Only one joint gap (the title sequence break) — no recap to extract.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(40),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval> { Interval(60, 120) },
            BlackIntervals = new List<DetectedInterval> { Interval(60, 120) }
        });

        result.Single(m => m.Type == MarkerType.Intro).End.Should().Be(TimeSpan.FromSeconds(120));
        result.Where(m => m.Type == MarkerType.Recap).Should().BeEmpty();
    }

    [Fact]
    public void Recap_is_skipped_when_no_gap_starts_within_recap_window()
    {
        // First gap is at 300s (outside the 90s EpisodeRecapWindow) so we can't
        // confidently call it a recap. Intro still detected on the second gap.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(40),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval> { Interval(300, 310), Interval(400, 410) },
            BlackIntervals = new List<DetectedInterval> { Interval(300, 310), Interval(400, 410) }
        });

        result.Where(m => m.Type == MarkerType.Recap).Should().BeEmpty();
        result.Single(m => m.Type == MarkerType.Intro).End.Should().Be(TimeSpan.FromSeconds(410));
    }

    [Fact]
    public void Movies_do_not_get_recap_markers()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(120),
            IsEpisode = false,
            SilenceIntervals = new List<DetectedInterval> { Interval(30, 40), Interval(60, 120) },
            BlackIntervals = new List<DetectedInterval> { Interval(30, 40), Interval(60, 120) }
        });

        result.Where(m => m.Type == MarkerType.Recap).Should().BeEmpty();
    }

    [Fact]
    public void Detects_credits_starting_in_the_last_40_percent_of_duration()
    {
        // 90-minute movie, credits gap at 80 minutes (≥ 60% threshold) → credits 80m..90m
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(90),
            SilenceIntervals = new List<DetectedInterval> { Interval(4800, 4860) },
            BlackIntervals = new List<DetectedInterval> { Interval(4800, 4860) }
        });

        var credits = result.Single(m => m.Type == MarkerType.Credits);
        credits.Start.Should().Be(TimeSpan.FromSeconds(4800));
        credits.End.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void Credits_skips_a_late_act_break_and_lands_on_the_real_credits_roll()
    {
        // ~22.5-min episode (Batman: TAS shape): a commercial act break (~15:45)
        // and a scene fade (~17:34) both fall past the 60% mark, but the real
        // credits roll is the gap near the very end (~21:51). The old "first gap
        // past 60%" grabbed the act break and mislabeled 6 min of episode as
        // credits — auto-skipping real content and carving a bogus preview.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromSeconds(1347),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval>
            {
                Interval(30, 90),      // intro
                Interval(945, 950),    // act break ~15:45
                Interval(1054, 1058),  // scene fade ~17:34
                Interval(1311, 1315)   // real credits ~21:51
            },
            BlackIntervals = new List<DetectedInterval>
            {
                Interval(30, 90),
                Interval(945, 950),
                Interval(1054, 1058),
                Interval(1311, 1315)
            }
        });

        var credits = result.Single(m => m.Type == MarkerType.Credits);
        credits.Start.Should().Be(TimeSpan.FromSeconds(1311));
        credits.End.Should().Be(TimeSpan.FromSeconds(1347));
        // No bogus mid-episode "preview" carved out of the act-break region.
        result.Where(m => m.Type == MarkerType.Preview).Should().BeEmpty();
    }

    [Fact]
    public void DetectCredits_false_suppresses_credits_but_keeps_intro()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(90),
            SilenceIntervals = new List<DetectedInterval> { Interval(30, 90), Interval(4800, 4860) },
            BlackIntervals = new List<DetectedInterval> { Interval(30, 90), Interval(4800, 4860) },
            DetectCredits = false
        });

        result.Should().Contain(m => m.Type == MarkerType.Intro);
        result.Where(m => m.Type == MarkerType.Credits).Should().BeEmpty();
    }

    [Fact]
    public void DetectIntro_false_suppresses_intro_but_keeps_credits()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(90),
            SilenceIntervals = new List<DetectedInterval> { Interval(30, 90), Interval(4800, 4860) },
            BlackIntervals = new List<DetectedInterval> { Interval(30, 90), Interval(4800, 4860) },
            DetectIntro = false
        });

        result.Where(m => m.Type == MarkerType.Intro).Should().BeEmpty();
        result.Should().Contain(m => m.Type == MarkerType.Credits);
    }

    [Fact]
    public void Returned_markers_are_ordered_by_start_time()
    {
        // 45-min episode: recap, intro, credits. Markers should come out in time order.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(45),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval>
            {
                Interval(30, 40),       // recap
                Interval(60, 120),      // intro
                Interval(2520, 2580)    // credits (~42 min, past 60% threshold of 27 min)
            },
            BlackIntervals = new List<DetectedInterval>
            {
                Interval(30, 40),
                Interval(60, 120),
                Interval(2520, 2580)
            }
        });

        var starts = result.Select(m => m.Start.TotalSeconds).ToList();
        starts.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Detects_credits_scenes_for_movies_when_stingers_expected()
    {
        // 100-min movie. Credits start at 4800s. A "scene" lives BETWEEN two joint gaps:
        // gap[0].End (4810) -> gap[1].Start (5000). Length = 190s > MinStingerLength (8s).
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(100),
            IsEpisode = false,
            ExpectsPostCreditsStinger = true,
            SilenceIntervals = new List<DetectedInterval>
            {
                Interval(4800, 4810),  // credits start
                Interval(5000, 5010),  // gap before/after the scene
                Interval(5100, 5110)
            },
            BlackIntervals = new List<DetectedInterval>
            {
                Interval(4800, 4810),
                Interval(5000, 5010),
                Interval(5100, 5110)
            }
        });

        var stingers = result.Where(m => m.Type == MarkerType.CreditsScene).ToList();
        stingers.Should().HaveCountGreaterThanOrEqualTo(1);
        stingers[0].Order.Should().Be(1);
        stingers[0].Start.Should().Be(TimeSpan.FromSeconds(4810));
        stingers[0].End.Should().Be(TimeSpan.FromSeconds(5000));
    }

    [Fact]
    public void Skips_credits_scenes_for_episodes_emits_preview_instead()
    {
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(45),
            IsEpisode = true,
            SilenceIntervals = new List<DetectedInterval>
            {
                Interval(2520, 2530),  // credits start at 42 min
                Interval(2580, 2590),  // gap before preview
                Interval(2640, 2650)   // gap after preview
            },
            BlackIntervals = new List<DetectedInterval>
            {
                Interval(2520, 2530),
                Interval(2580, 2590),
                Interval(2640, 2650)
            }
        });

        result.Where(m => m.Type == MarkerType.CreditsScene).Should().BeEmpty();
        var preview = result.SingleOrDefault(m => m.Type == MarkerType.Preview);
        preview.Should().NotBeNull();
    }

    [Fact]
    public void Stinger_count_is_capped_at_expected_count()
    {
        // ExpectsMidCreditsStinger=true + ExpectsPostCreditsStinger=true → up to 2 stingers
        // With 4 gaps in credits we'd have 3 possible inter-gap scenes; should only take first 2.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(120),
            IsEpisode = false,
            ExpectsMidCreditsStinger = true,
            ExpectsPostCreditsStinger = true,
            SilenceIntervals = new List<DetectedInterval>
            {
                Interval(4500, 4510),
                Interval(5000, 5010),
                Interval(5500, 5510),
                Interval(6000, 6010),
                Interval(6500, 6510)
            },
            BlackIntervals = new List<DetectedInterval>
            {
                Interval(4500, 4510),
                Interval(5000, 5010),
                Interval(5500, 5510),
                Interval(6000, 6010),
                Interval(6500, 6510)
            }
        });

        var stingers = result.Where(m => m.Type == MarkerType.CreditsScene).ToList();
        stingers.Should().HaveCountLessThanOrEqualTo(2);
        stingers.Select(s => s.Order).Should().BeEquivalentTo(stingers.Select((_, i) => i + 1));
    }

    [Fact]
    public void Joint_gap_calculation_only_emits_overlap_of_silence_and_black()
    {
        // Silence: 10-30s. Black: 20-40s. Joint: 20-30s.
        var result = _assembler.Assemble(new MarkerAssemblerInput
        {
            Duration = TimeSpan.FromMinutes(20),
            SilenceIntervals = new List<DetectedInterval> { Interval(10, 30) },
            BlackIntervals = new List<DetectedInterval> { Interval(20, 40) }
        });

        // Joint gap is 20-30s — within the 8-minute intro window, so we get an intro marker
        // running from 0 to 30s (gap end).
        var intro = result.Single(m => m.Type == MarkerType.Intro);
        intro.End.Should().Be(TimeSpan.FromSeconds(30));
    }
}
