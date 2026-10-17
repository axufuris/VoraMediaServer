namespace Vora.Infrastructure.Transcoding;

internal enum ThrottleStep
{
    Keep,
    Pause,
    Resume,
}

internal static class TranscodeThrottle
{
    public const int MinimumBufferSeconds = 30;

    public static ThrottleStep Decide(int bufferSetting, bool canPause, bool paused, SegmentPlan plan, int watchedSegment, int leadingEdge)
    {
        if (!canPause || bufferSetting <= 0 || plan.Count == 0 || watchedSegment < 0 || leadingEdge < watchedSegment)
        {
            return paused ? ThrottleStep.Resume : ThrottleStep.Keep;
        }

        var buffer = Math.Max(bufferSetting, MinimumBufferSeconds);
        var ahead = SecondsAhead(plan, watchedSegment, leadingEdge);
        if (!paused && ahead >= buffer) return ThrottleStep.Pause;
        if (paused && ahead < buffer / 2.0) return ThrottleStep.Resume;
        return ThrottleStep.Keep;
    }

    public static double SecondsAhead(SegmentPlan plan, int watchedSegment, int leadingEdge)
    {
        var last = Math.Min(leadingEdge, plan.Count - 1);
        return plan.StartOf(last) + plan.DurationOf(last) - plan.StartOf(watchedSegment);
    }
}
