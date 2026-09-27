namespace Vora.Application.Streaming;

// What a progress ping means for an item's watched state.
//
// The flag used to be sticky — `IsPlayed || completed` — so once an item had
// been finished it stayed "watched" forever. Starting it again recorded a resume
// position that nothing would show: the details page kept the watched check and
// offered Play rather than Resume, and Continue Watching filtered the item out
// because it was still marked played.
//
// It is now derived from where the viewer actually is, with one guard: a ping
// can land at position 0 before the player has seeked to its resume point, and
// that must not wipe a finished item's state. So clearing only happens once
// playback is meaningfully under way.
public static class WatchStateTransition
{
    public const double CompletionFraction = 0.90;
    public const double RestartGraceSeconds = 15;

    public static bool IsComplete(double positionSeconds, double durationSeconds) =>
        durationSeconds > 0 && (positionSeconds / durationSeconds) >= CompletionFraction;

    // True when the viewer is far enough into a re-watch that the item is no
    // longer "finished". Below the grace window the previous state stands.
    public static bool IsRestarted(double positionSeconds, double durationSeconds) =>
        durationSeconds > 0
        && !IsComplete(positionSeconds, durationSeconds)
        && positionSeconds >= RestartGraceSeconds;

    public static bool ResolveIsPlayed(bool wasPlayed, double positionSeconds, double durationSeconds)
    {
        if (IsComplete(positionSeconds, durationSeconds)) return true;
        if (IsRestarted(positionSeconds, durationSeconds)) return false;

        return wasPlayed;
    }

    // A finished item has nothing to resume; anything else resumes where it is.
    public static double ResolveResumePosition(double positionSeconds, double durationSeconds) =>
        IsComplete(positionSeconds, durationSeconds) ? 0 : positionSeconds;
}
