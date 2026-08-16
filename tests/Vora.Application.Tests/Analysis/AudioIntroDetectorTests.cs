using Vora.Application.Analysis;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class AudioIntroDetectorTests
{
    private readonly AudioIntroDetector _detector = new(new AudioFingerprintComparer());

    private const double PointDuration = 0.1;

    private static uint[] Noise(int count, int seed)
    {
        var values = new uint[count];
        var state = (uint)(seed * 2654435761u + 1);
        for (var i = 0; i < count; i++)
        {
            state = state * 1664525u + 1013904223u;
            values[i] = state;
        }
        return values;
    }

    private static EpisodeFingerprint Episode(Guid id, uint[] points) => new()
    {
        MediaItemId = id,
        Points = points,
        PointDurationSeconds = PointDuration,
        Duration = TimeSpan.FromMinutes(30)
    };

    // Build N episodes that all share the same theme at the same index (start),
    // each over its own noise so nothing but the theme overlaps.
    private static List<EpisodeFingerprint> Season(int episodes, uint[] theme, int themeAt)
    {
        var list = new List<EpisodeFingerprint>();
        for (var e = 0; e < episodes; e++)
        {
            var points = Noise(2000, seed: 500 + e);
            for (var i = 0; i < theme.Length; i++) points[themeAt + i] = theme[i];
            list.Add(Episode(Guid.NewGuid(), points));
        }
        return list;
    }

    [Fact]
    public void Detects_a_shared_intro_across_a_season()
    {
        var theme = Noise(300, seed: 42); // 300 points * 0.1s = 30s intro
        var season = Season(episodes: 6, theme, themeAt: 150); // starts at 15s

        var result = _detector.DetectIntros(season, new AudioIntroDetectionOptions());

        result.Should().HaveCount(6);
        foreach (var (_, marker) in result)
        {
            marker.Type.Should().Be(MarkerType.Intro);
            marker.Start.Should().BeCloseTo(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));
            marker.End.Should().BeCloseTo(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void Emits_nothing_when_episodes_share_no_theme()
    {
        var season = new List<EpisodeFingerprint>();
        for (var e = 0; e < 5; e++) season.Add(Episode(Guid.NewGuid(), Noise(2000, seed: 900 + e)));

        var result = _detector.DetectIntros(season, new AudioIntroDetectionOptions());

        result.Should().BeEmpty();
    }

    [Fact]
    public void A_single_episode_yields_nothing()
    {
        var theme = Noise(300, seed: 1);
        var season = Season(episodes: 1, theme, themeAt: 100);

        _detector.DetectIntros(season, new AudioIntroDetectionOptions()).Should().BeEmpty();
    }

    [Fact]
    public void An_odd_episode_out_does_not_get_a_false_intro()
    {
        var theme = Noise(300, seed: 77);
        var season = Season(episodes: 5, theme, themeAt: 150);
        // Add a bonus episode (e.g. a special) with no shared theme.
        var loner = Episode(Guid.NewGuid(), Noise(2000, seed: 4242));
        season.Add(loner);

        var result = _detector.DetectIntros(season, new AudioIntroDetectionOptions());

        result.Should().HaveCount(5);
        result.Should().NotContainKey(loner.MediaItemId);
    }

    [Fact]
    public void Quorum_of_one_is_enough_for_two_matching_episodes()
    {
        var theme = Noise(300, seed: 5);
        var season = Season(episodes: 2, theme, themeAt: 150);

        var result = _detector.DetectIntros(season, new AudioIntroDetectionOptions { Quorum = 1 });

        result.Should().HaveCount(2);
    }
}
