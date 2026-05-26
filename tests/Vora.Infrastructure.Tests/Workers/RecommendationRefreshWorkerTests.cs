using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public class RecommendationRefreshWorkerTests
{
    [Fact]
    public void IsDueForRefresh_ManualOnly_never_returns_due()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var lastRefreshed = now.AddDays(-30);

        RecommendationRefreshWorker.IsDueForRefresh("ManualOnly", null, now).Should().BeFalse();
        RecommendationRefreshWorker.IsDueForRefresh("ManualOnly", lastRefreshed, now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_ManualOnly_case_insensitive()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        RecommendationRefreshWorker.IsDueForRefresh("manualonly", null, now).Should().BeFalse();
        RecommendationRefreshWorker.IsDueForRefresh("MANUALONLY", null, now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_Every6Hours_due_when_never_refreshed()
    {
        var now = DateTime.UtcNow;
        RecommendationRefreshWorker.IsDueForRefresh("Every6Hours", null, now).Should().BeTrue();
    }

    [Fact]
    public void IsDueForRefresh_Every6Hours_due_after_6_hours_elapsed()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var sixHoursAgo = now.AddHours(-6);
        var fiveHoursAgo = now.AddHours(-5);

        RecommendationRefreshWorker.IsDueForRefresh("Every6Hours", sixHoursAgo, now).Should().BeTrue();
        RecommendationRefreshWorker.IsDueForRefresh("Every6Hours", fiveHoursAgo, now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_Every12Hours_respects_threshold()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);

        RecommendationRefreshWorker.IsDueForRefresh("Every12Hours", now.AddHours(-12), now).Should().BeTrue();
        RecommendationRefreshWorker.IsDueForRefresh("Every12Hours", now.AddHours(-11), now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_Daily3am_due_when_past_3am_today_and_not_yet_refreshed_today()
    {
        var now = new DateTime(2026, 5, 26, 5, 0, 0, DateTimeKind.Utc); // 5am UTC
        var yesterdayRefresh = new DateTime(2026, 5, 25, 3, 0, 0, DateTimeKind.Utc);

        RecommendationRefreshWorker.IsDueForRefresh("Daily3am", yesterdayRefresh, now).Should().BeTrue();
    }

    [Fact]
    public void IsDueForRefresh_Daily3am_not_due_when_before_3am()
    {
        var now = new DateTime(2026, 5, 26, 2, 30, 0, DateTimeKind.Utc); // 2:30am UTC
        var yesterdayRefresh = new DateTime(2026, 5, 25, 3, 0, 0, DateTimeKind.Utc);

        RecommendationRefreshWorker.IsDueForRefresh("Daily3am", yesterdayRefresh, now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_Daily3am_not_due_when_already_refreshed_today()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        var todayRefresh = new DateTime(2026, 5, 26, 3, 30, 0, DateTimeKind.Utc);

        RecommendationRefreshWorker.IsDueForRefresh("Daily3am", todayRefresh, now).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_Daily3am_due_when_never_refreshed_and_past_target_hour()
    {
        var now = new DateTime(2026, 5, 26, 5, 0, 0, DateTimeKind.Utc);
        RecommendationRefreshWorker.IsDueForRefresh("Daily3am", null, now).Should().BeTrue();
    }

    [Fact]
    public void IsDueForRefresh_WeeklySunday3am_only_due_on_sunday_after_3am()
    {
        var sundayAfter3 = new DateTime(2026, 5, 24, 4, 0, 0, DateTimeKind.Utc); // Sunday 4am UTC
        var sundayBefore3 = new DateTime(2026, 5, 24, 2, 0, 0, DateTimeKind.Utc);
        var monday = new DateTime(2026, 5, 25, 4, 0, 0, DateTimeKind.Utc);

        sundayAfter3.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        monday.DayOfWeek.Should().Be(DayOfWeek.Monday);

        RecommendationRefreshWorker.IsDueForRefresh("WeeklySunday3am", null, sundayAfter3).Should().BeTrue();
        RecommendationRefreshWorker.IsDueForRefresh("WeeklySunday3am", null, sundayBefore3).Should().BeFalse();
        RecommendationRefreshWorker.IsDueForRefresh("WeeklySunday3am", null, monday).Should().BeFalse();
    }

    [Fact]
    public void IsDueForRefresh_WeeklySunday3am_requires_at_least_seven_days_since_last()
    {
        var thisSunday = new DateTime(2026, 5, 24, 4, 0, 0, DateTimeKind.Utc);
        var sixDaysAgo = thisSunday.AddDays(-6); // within the 6.9-day grace
        var sevenDaysAgo = thisSunday.AddDays(-7);

        RecommendationRefreshWorker.IsDueForRefresh("WeeklySunday3am", sixDaysAgo, thisSunday).Should().BeFalse();
        RecommendationRefreshWorker.IsDueForRefresh("WeeklySunday3am", sevenDaysAgo, thisSunday).Should().BeTrue();
    }

    [Fact]
    public void IsDueForRefresh_unknown_preset_falls_back_to_Daily3am()
    {
        var now = new DateTime(2026, 5, 26, 5, 0, 0, DateTimeKind.Utc);
        RecommendationRefreshWorker.IsDueForRefresh("GibberishPreset", null, now).Should().BeTrue();

        var beforeTarget = new DateTime(2026, 5, 26, 2, 0, 0, DateTimeKind.Utc);
        RecommendationRefreshWorker.IsDueForRefresh("GibberishPreset", null, beforeTarget).Should().BeFalse();
    }

    [Fact]
    public void IsWeeklyDue_true_when_never_refreshed()
    {
        RecommendationRefreshWorker.IsWeeklyDue(null, DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsWeeklyDue_true_when_seven_days_have_passed()
    {
        var now = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc);
        RecommendationRefreshWorker.IsWeeklyDue(now.AddDays(-7), now).Should().BeTrue();
        RecommendationRefreshWorker.IsWeeklyDue(now.AddDays(-6.5), now).Should().BeFalse();
    }
}
