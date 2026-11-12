using Vora.Application.Recommendations;

namespace Vora.Application.Tests.Recommendations;

public class RecommendationFreshnessTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    // The AI's rows cost a chat completion and an embeddings request, so they
    // last as long as the memory cache kept them, whatever was watched since.
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(11.99, false)]
    [InlineData(12.0, true)]
    [InlineData(30.0, true)]
    public void Rows_from_an_outside_call_last_twelve_hours(double ageHours, bool stale)
    {
        RecommendationFreshness.IsStale(makesOutsideCalls: true, Now.AddHours(-ageHours), lastActivityAt: Now, Now).Should().Be(stale);
    }

    [Theory]
    [InlineData(1.0, null, false)]
    [InlineData(1.0, 2.0, false)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(1.0, 0.5, true)]
    [InlineData(23.99, 30.0, false)]
    [InlineData(24.0, 30.0, true)]
    public void Local_rows_last_until_the_profile_plays_or_rates_something_or_a_day(double ageHours, double? activityHoursAgo, bool stale)
    {
        DateTime? activity = activityHoursAgo is { } hours ? Now.AddHours(-hours) : null;

        RecommendationFreshness.IsStale(makesOutsideCalls: false, Now.AddHours(-ageHours), activity, Now).Should().Be(stale);
    }
}
