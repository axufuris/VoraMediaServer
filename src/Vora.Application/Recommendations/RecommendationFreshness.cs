namespace Vora.Application.Recommendations;

// When a provider's stored For You rows are out of date. Out-of-date rows are
// still shown, and a rebuild is queued behind them.
public static class RecommendationFreshness
{
    // Rows that cost an outside call (the AI's chat and embedding requests)
    // are kept as long as the memory cache used to keep them, so token spend
    // doesn't go up.
    public static readonly TimeSpan OutsideCallLifetime = TimeSpan.FromHours(12);

    // Rows built from the library alone are rebuilt once the profile has played
    // or rated anything since, and at least daily for titles added or gone.
    public static readonly TimeSpan LocalLifetime = TimeSpan.FromHours(24);

    public static bool IsStale(bool makesOutsideCalls, DateTime generatedAt, DateTime? lastActivityAt, DateTime now) =>
        makesOutsideCalls
            ? now - generatedAt >= OutsideCallLifetime
            : now - generatedAt >= LocalLifetime || lastActivityAt > generatedAt;
}
