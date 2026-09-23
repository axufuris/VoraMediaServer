namespace Vora.Application.Media.ViewModels;

// A post that does not qualify is discarded rather than rejected — it is a
// correct outcome, not a client error, so it is not a 4xx. That left no way to
// tell a stored play from a dropped one, which matters most to the people it is
// hardest for: a native client author sees plays simply not appear, days later,
// with nothing in their own logs to explain it.
public enum RecordPlayOutcome
{
    Recorded = 0,

    // The listen was too shallow to count. Almost always the client posting at
    // its own threshold instead of the server's, or posting mid-track rather
    // than when the listen ended.
    BelowThreshold = 1,

    // No track with that id. A different problem entirely, and one a client
    // author would otherwise spend a long time looking for in their own
    // threshold logic.
    UnknownTrack = 2,
}

public class RecordPlayResponse
{
    public bool Recorded { get; set; }

    // Serialized as a string by the global JsonStringEnumConverter, so a client
    // reads "BelowThreshold" rather than a number whose meaning depends on the
    // order the enum happens to be declared in.
    public RecordPlayOutcome Outcome { get; set; }
}
