namespace Vora.Infrastructure.Transcoding;

internal sealed record SegmentLaunch(int StartSegment, double SeekSeconds, double TimestampOffsetSeconds, IReadOnlyList<double>? CutTimes)
{
    public bool Seeks => SeekSeconds > 0;

    public bool FollowsKeyframes => CutTimes is not null;

    public static SegmentLaunch Uniform(int segment)
    {
        double start = segment * SegmentPlan.SegmentSeconds;
        return new SegmentLaunch(segment, start, start, null);
    }
}

internal sealed class SegmentPlan
{
    public const int SegmentSeconds = 4;

    private const double SeekLeadSeconds = 0.25;
    private const double CutLeadSeconds = 0.15;
    private const double ShortestGapBeforeBoundary = 0.3;
    private const double ShortestGapAfterBoundary = 0.4;
    private const double LatestFirstKeyframe = 0.5;
    private const double ShortestFinalSegment = 0.5;
    private const double TimelineOffsetSeconds = 1.0;
    private const double LastSegmentRoundingSeconds = 0.01;

    private readonly double[] _starts;
    private readonly double _end;

    private SegmentPlan(double[] starts, double end, bool followsKeyframes)
    {
        _starts = starts;
        _end = end;
        FollowsKeyframes = followsKeyframes;
    }

    public bool FollowsKeyframes { get; }

    public int Count => _starts.Length;

    public int TargetDuration => FollowsKeyframes
        ? Math.Max(1, (int)Math.Ceiling(Enumerable.Range(0, Count).Max(DurationOf) - 0.001))
        : SegmentSeconds;

    public double StartOf(int segment) => _starts[Math.Clamp(segment, 0, Count - 1)];

    public double DurationOf(int segment) => (segment + 1 < Count ? _starts[segment + 1] : _end) - _starts[segment];

    public int IndexAt(double seconds)
    {
        int index = Array.BinarySearch(_starts, Math.Max(0, seconds));
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    public SegmentLaunch LaunchAt(int segment)
    {
        if (!FollowsKeyframes) return SegmentLaunch.Uniform(segment);

        double seek = segment > 0 ? _starts[segment] + SeekLeadSeconds : 0;
        var cuts = _starts.Skip(segment + 1).Select(start => start - seek - CutLeadSeconds).ToList();
        return new SegmentLaunch(segment, seek, seek + TimelineOffsetSeconds, cuts);
    }

    public static SegmentPlan Uniform(double durationSeconds)
    {
        int count = Math.Max(1, (int)Math.Ceiling(durationSeconds / SegmentSeconds));
        var starts = Enumerable.Range(0, count).Select(index => index * (double)SegmentSeconds).ToArray();
        double last = durationSeconds - (count - 1) * SegmentSeconds;
        double end = last <= LastSegmentRoundingSeconds ? count * (double)SegmentSeconds : durationSeconds;
        return new SegmentPlan(starts, end, followsKeyframes: false);
    }

    public static SegmentPlan? FromKeyframes(IReadOnlyList<double> keyframes, double durationSeconds)
    {
        var keys = keyframes.Distinct().Order().ToArray();
        if (durationSeconds <= 0 || keys.Length == 0 || keys[0] > LatestFirstKeyframe || TooDenseToCut(keys)) return null;

        var starts = new List<double> { 0 };
        for (int i = 1; i < keys.Length; i++)
        {
            double keyframe = keys[i];
            if (keyframe > durationSeconds - ShortestFinalSegment) break;
            if (keyframe < starts[^1] + SegmentSeconds) continue;

            bool previousTooClose = keyframe - keys[i - 1] < ShortestGapBeforeBoundary;
            bool nextTooClose = i + 1 < keys.Length && keys[i + 1] - keyframe < ShortestGapAfterBoundary;
            if (!previousTooClose && !nextTooClose) starts.Add(keyframe);
        }
        return new SegmentPlan([.. starts], durationSeconds, followsKeyframes: true);
    }

    private static bool TooDenseToCut(double[] keys)
    {
        if (keys.Length < 3) return false;
        var gaps = keys.Zip(keys.Skip(1), (earlier, later) => later - earlier).Order().ToArray();
        return gaps[gaps.Length / 2] < ShortestGapAfterBoundary;
    }
}
