using Vora.Application.Analysis;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class MarkerMergeTests
{
    private static DetectedMarker M(MarkerType type, double startSec, double endSec) => new()
    {
        Type = type,
        Start = TimeSpan.FromSeconds(startSec),
        End = TimeSpan.FromSeconds(endSec)
    };

    [Fact]
    public void Fingerprint_intro_beats_silence_black_intro()
    {
        var sb = new List<DetectedMarker> { M(MarkerType.Intro, 0, 60), M(MarkerType.Credits, 1300, 1400) };
        var fp = M(MarkerType.Intro, 90, 150);

        var result = MarkerMerge.Resolve(new List<DetectedMarker>(), fp, sb, detectIntro: true);

        var intro = result.Single(m => m.Type == MarkerType.Intro);
        intro.Start.Should().Be(TimeSpan.FromSeconds(90));
        intro.End.Should().Be(TimeSpan.FromSeconds(150));
        result.Should().ContainSingle(m => m.Type == MarkerType.Credits);
    }

    [Fact]
    public void Chapter_intro_beats_the_fingerprint_intro()
    {
        var chapters = new List<DetectedMarker> { M(MarkerType.Intro, 30, 75) };
        var fp = M(MarkerType.Intro, 90, 150);
        var sb = new List<DetectedMarker> { M(MarkerType.Intro, 0, 60) };

        var result = MarkerMerge.Resolve(chapters, fp, sb, detectIntro: true);

        result.Single(m => m.Type == MarkerType.Intro).End.Should().Be(TimeSpan.FromSeconds(75));
    }

    [Fact]
    public void Fingerprint_intro_is_ignored_when_intro_detection_is_off()
    {
        var sb = new List<DetectedMarker> { M(MarkerType.Credits, 1300, 1400) };
        var fp = M(MarkerType.Intro, 90, 150);

        var result = MarkerMerge.Resolve(new List<DetectedMarker>(), fp, sb, detectIntro: false);

        result.Should().NotContain(m => m.Type == MarkerType.Intro);
    }

    [Fact]
    public void Recap_credits_preview_and_scenes_come_from_silence_black_when_unchaptered()
    {
        var sb = new List<DetectedMarker>
        {
            M(MarkerType.Intro, 0, 90),
            M(MarkerType.Recap, 0, 40),
            M(MarkerType.Credits, 1300, 1400),
            M(MarkerType.Preview, 1360, 1400),
            M(MarkerType.CreditsScene, 1345, 1355)
        };

        var result = MarkerMerge.Resolve(new List<DetectedMarker>(), fingerprintIntro: null, sb, detectIntro: true);

        result.Select(m => m.Type).Should().Contain(new[]
        {
            MarkerType.Intro, MarkerType.Recap, MarkerType.Credits, MarkerType.Preview, MarkerType.CreditsScene
        });
    }

    [Fact]
    public void Chapter_credits_beats_silence_black_credits()
    {
        var chapters = new List<DetectedMarker> { M(MarkerType.Credits, 1290, 1400) };
        var sb = new List<DetectedMarker> { M(MarkerType.Credits, 1310, 1400) };

        var result = MarkerMerge.Resolve(chapters, fingerprintIntro: null, sb, detectIntro: true);

        result.Single(m => m.Type == MarkerType.Credits).Start.Should().Be(TimeSpan.FromSeconds(1290));
    }

    [Fact]
    public void Output_is_ordered_by_start()
    {
        var sb = new List<DetectedMarker>
        {
            M(MarkerType.Credits, 1300, 1400),
            M(MarkerType.Intro, 0, 90)
        };

        var result = MarkerMerge.Resolve(new List<DetectedMarker>(), fingerprintIntro: null, sb, detectIntro: true);

        result.Should().BeInAscendingOrder(m => m.Start);
    }
}
