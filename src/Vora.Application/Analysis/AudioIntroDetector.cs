using Vora.Domain.Entities.Media;

namespace Vora.Application.Analysis;

public class EpisodeFingerprint
{
    public required Guid MediaItemId { get; init; }
    public required uint[] Points { get; init; }
    public required double PointDurationSeconds { get; init; }
    public required TimeSpan Duration { get; init; }
}

public class AudioIntroDetectionOptions
{
    public int MaxBitError { get; init; } = 6;
    public int GapTolerance { get; init; } = 3;
    public int MinIntroSeconds { get; init; } = 10;
    public int Quorum { get; init; } = 2;
    public int SampleSize { get; init; } = 8;
    public double ClusterToleranceSeconds { get; init; } = 3;
}

public interface IAudioIntroDetector
{
    IReadOnlyDictionary<Guid, DetectedMarker> DetectIntros(
        IReadOnlyList<EpisodeFingerprint> episodes,
        AudioIntroDetectionOptions options);
}

// Season-relative intro detection. For each episode it finds where the shared
// theme sits by comparing against a sample of its siblings, then keeps the intro
// only when a quorum of those comparisons agree on the same location — a single
// coincidental match never wins. Emits one Intro marker per confident episode.
public class AudioIntroDetector : IAudioIntroDetector
{
    private readonly IAudioFingerprintComparer _comparer;

    public AudioIntroDetector(IAudioFingerprintComparer comparer)
    {
        _comparer = comparer;
    }

    public IReadOnlyDictionary<Guid, DetectedMarker> DetectIntros(
        IReadOnlyList<EpisodeFingerprint> episodes,
        AudioIntroDetectionOptions options)
    {
        var result = new Dictionary<Guid, DetectedMarker>();
        if (episodes.Count < 2) return result;

        foreach (var episode in episodes)
        {
            if (episode.Points.Length == 0 || episode.PointDurationSeconds <= 0) continue;

            var minRunPoints = (int)Math.Round(options.MinIntroSeconds / episode.PointDurationSeconds);
            if (minRunPoints <= 0) continue;

            var candidates = new List<(TimeSpan Start, TimeSpan End)>();
            var compared = 0;

            foreach (var other in episodes)
            {
                if (other.MediaItemId == episode.MediaItemId || other.Points.Length == 0) continue;
                if (compared >= options.SampleSize) break;
                compared++;

                var match = _comparer.FindSharedSegment(
                    episode.Points, other.Points, options.MaxBitError, options.GapTolerance, minRunPoints);
                if (match == null) continue;

                var start = TimeSpan.FromSeconds(match.Value.StartIndexA * episode.PointDurationSeconds);
                var end = TimeSpan.FromSeconds((match.Value.EndIndexA + 1) * episode.PointDurationSeconds);
                candidates.Add((start, end));
            }

            var intro = Resolve(candidates, options.Quorum, TimeSpan.FromSeconds(options.ClusterToleranceSeconds));
            if (intro != null) result[episode.MediaItemId] = intro;
        }

        return result;
    }

    private static DetectedMarker? Resolve(
        List<(TimeSpan Start, TimeSpan End)> candidates,
        int quorum,
        TimeSpan tolerance)
    {
        if (candidates.Count < quorum) return null;

        var medianStart = Median(candidates.Select(c => c.Start).ToList());
        var agreeing = candidates
            .Where(c => (c.Start - medianStart).Duration() <= tolerance)
            .ToList();
        if (agreeing.Count < quorum) return null;

        return new DetectedMarker
        {
            Type = MarkerType.Intro,
            Start = Median(agreeing.Select(c => c.Start).ToList()),
            End = Median(agreeing.Select(c => c.End).ToList())
        };
    }

    private static TimeSpan Median(List<TimeSpan> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
