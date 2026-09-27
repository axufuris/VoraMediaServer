using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class ChapterMarkerMapperTests
{
    private static MediaChapter Chapter(string? title, double startSec, double endSec) => new()
    {
        Title = title,
        Start = TimeSpan.FromSeconds(startSec),
        End = TimeSpan.FromSeconds(endSec)
    };

    private static ChapterMarkerResult Map(
        IReadOnlyList<MediaChapter> chapters,
        double durationSec,
        bool isEpisode = true,
        bool detectIntro = true,
        bool detectCredits = true,
        bool detectPreview = true) =>
        ChapterMarkerMapper.Map(chapters, TimeSpan.FromSeconds(durationSec), isEpisode, detectIntro, detectCredits, detectPreview);

    [Fact]
    public void Named_intro_and_credits_chapters_produce_both_markers_and_full_cover()
    {
        var result = Map(new[]
        {
            Chapter("Intro", 0, 90),
            Chapter("Episode", 90, 1300),
            Chapter("End Credits", 1300, 1400)
        }, durationSec: 1400);

        result.Markers.Should().ContainSingle(m => m.Type == MarkerType.Intro)
            .Which.End.Should().Be(TimeSpan.FromSeconds(90));
        result.Markers.Should().ContainSingle(m => m.Type == MarkerType.Credits)
            .Which.Start.Should().Be(TimeSpan.FromSeconds(1300));
        result.Covers(detectIntro: true, detectCredits: true).Should().BeTrue();
    }

    [Fact]
    public void Opening_credits_chapter_is_classified_as_intro_not_credits()
    {
        var result = Map(new[]
        {
            Chapter("Opening Credits", 0, 75),
            Chapter("Show", 75, 1400)
        }, durationSec: 1400);

        result.Markers.Should().ContainSingle(m => m.Type == MarkerType.Intro);
        result.Markers.Should().NotContain(m => m.Type == MarkerType.Credits);
    }

    [Fact]
    public void Unnamed_scene_chapters_produce_no_markers_and_no_cover()
    {
        var result = Map(new[]
        {
            Chapter("Chapter 1", 0, 300),
            Chapter("Chapter 2", 300, 900),
            Chapter("Chapter 3", 900, 1400)
        }, durationSec: 1400);

        result.Markers.Should().BeEmpty();
        result.Covers(detectIntro: true, detectCredits: true).Should().BeFalse();
    }

    [Fact]
    public void Recap_opening_and_credits_all_map_for_an_episode()
    {
        var result = Map(new[]
        {
            Chapter("Previously On", 0, 40),
            Chapter("Opening Titles", 40, 100),
            Chapter("End Credits", 1300, 1400)
        }, durationSec: 1400);

        result.Markers.Select(m => m.Type).Should()
            .Contain(new[] { MarkerType.Recap, MarkerType.Intro, MarkerType.Credits });
    }

    [Fact]
    public void Preview_chapter_only_emitted_when_preview_detection_is_on()
    {
        var chapters = new[]
        {
            Chapter("Intro", 0, 90),
            Chapter("End Credits", 1300, 1360),
            Chapter("Next Episode Preview", 1360, 1400)
        };

        Map(chapters, durationSec: 1400, detectPreview: true).Markers
            .Should().Contain(m => m.Type == MarkerType.Preview);
        Map(chapters, durationSec: 1400, detectPreview: false).Markers
            .Should().NotContain(m => m.Type == MarkerType.Preview);
    }

    [Fact]
    public void A_credits_named_chapter_early_in_the_file_is_ignored()
    {
        // "credit" in the first 40% is not the credits roll (e.g. a mid-file art
        // card); the credits search only trusts chapters in the tail.
        var result = Map(new[]
        {
            Chapter("Intro", 0, 90),
            Chapter("Credit Cookie", 300, 340),
            Chapter("Show", 340, 1400)
        }, durationSec: 1400);

        result.Markers.Should().NotContain(m => m.Type == MarkerType.Credits);
        result.CoversCredits.Should().BeFalse();
    }

    [Fact]
    public void Movies_do_not_get_recap_or_preview_markers()
    {
        var result = Map(new[]
        {
            Chapter("Previously", 0, 40),
            Chapter("Opening", 40, 100),
            Chapter("End Credits", 6000, 6600),
            Chapter("Next Time", 6600, 6800)
        }, durationSec: 6800, isEpisode: false);

        result.Markers.Should().NotContain(m => m.Type == MarkerType.Recap);
        result.Markers.Should().NotContain(m => m.Type == MarkerType.Preview);
        result.Markers.Should().Contain(m => m.Type == MarkerType.Intro);
        result.Markers.Should().Contain(m => m.Type == MarkerType.Credits);
    }

    [Fact]
    public void Cover_is_false_when_only_intro_is_present_but_credits_still_wanted()
    {
        var result = Map(new[]
        {
            Chapter("Intro", 0, 90),
            Chapter("Show", 90, 1400)
        }, durationSec: 1400);

        result.Covers(detectIntro: true, detectCredits: true).Should().BeFalse();
        result.Covers(detectIntro: true, detectCredits: false).Should().BeTrue();
    }

    [Fact]
    public void Degenerate_chapters_are_skipped()
    {
        var result = Map(new[]
        {
            Chapter("Intro", 90, 90),      // zero-length
            Chapter("End Credits", 1400, 1500) // ends past duration
        }, durationSec: 1400);

        result.Markers.Should().BeEmpty();
    }
}
