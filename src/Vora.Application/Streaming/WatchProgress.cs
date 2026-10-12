namespace Vora.Application.Streaming;

public static class WatchProgress
{
    private const double PlayedThroughShare = 0.5;

    public static double WatchedSeconds(DateTime startedAt, DateTime endedAt, double pausedSeconds) =>
        Math.Max(0, (endedAt - startedAt).TotalSeconds - pausedSeconds);

    public static int PercentComplete(double startPosition, double endPosition, double watchedSeconds, double durationSeconds)
    {
        if (durationSeconds <= 0) return 0;

        var crossed = Math.Max(0, endPosition - startPosition);
        var reached = watchedSeconds >= crossed * PlayedThroughShare
            ? endPosition
            : startPosition + watchedSeconds;
        return (int)Math.Clamp(reached / durationSeconds * 100, 0, 100);
    }
}
