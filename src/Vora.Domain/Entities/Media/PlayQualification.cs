namespace Vora.Domain.Entities.Media;

// Whether a listen counts as a play at all, and how much it counts for once it
// does. Both questions come from the same place because they are the same
// judgement at two strengths: the gate rejects a listen too shallow to mean
// anything, and the weight separates the rest by how much of the track was
// actually heard.
//
// The rule this replaces let a listen count after thirty seconds OR half the
// track, whichever came first. On a two-minute song the thirty-second arm fired
// at a quarter of the way through, so abandoning a track early scored exactly
// the same as playing it out — and since nothing downstream looked at how long
// anyone listened, that was the end of it.
public static class PlayQualification
{
    // Four minutes is the point past which a listen counts regardless of the
    // track's length, so a twenty-minute piece does not have to run ten minutes
    // to register. Below that the test is proportional, which is what stops a
    // short track qualifying on a fraction of itself.
    public const int LongListenSeconds = 240;
    public const double MinimumFraction = 0.5;

    // Nothing to take a fraction OF. A track with no scanned duration falls back
    // to an absolute threshold, because the alternative is that it can never
    // qualify at all.
    public const int UnknownDurationSeconds = 30;

    public const double NeutralWeight = 0.5;

    public static bool Qualifies(int secondsListened, int? trackDurationSeconds)
    {
        if (secondsListened <= 0) return false;
        if (trackDurationSeconds is not > 0) return secondsListened >= UnknownDurationSeconds;

        return secondsListened >= LongListenSeconds
            || (double)secondsListened / trackDurationSeconds.Value >= MinimumFraction;
    }

    // Only ever called for listens that already qualified, so the floor is the
    // weight of a listen that just cleared the gate rather than of a skip.
    public static double Weight(int secondsListened, int? trackDurationSeconds, bool completed)
    {
        if (completed) return 1.0;

        // Either the track's length was never scanned or the listen's depth was
        // never recorded. Both are an absence of evidence rather than evidence of
        // a shallow listen, so they sit in the middle instead of being punished.
        if (trackDurationSeconds is not > 0 || secondsListened <= 0) return NeutralWeight;

        var fraction = (double)secondsListened / trackDurationSeconds.Value;

        // Players stop firing timeupdate a little before the end, and a track
        // followed straight by the next one can miss its own ended event, so the
        // last few percent count as played out.
        if (fraction >= 0.95) return 1.0;
        if (fraction >= 0.75) return 0.8;
        if (fraction >= MinimumFraction) return 0.5;

        return 0.25;
    }
}
