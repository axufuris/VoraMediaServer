using Vora.Application.Backups;
using Vora.Application.Settings;

namespace Vora.Application.Tests.Backups;

public class BackupScheduleEvaluatorTests
{
    private static readonly TimeZoneInfo Chicago = ScheduleClock.Resolve("America/Chicago");
    private static readonly TimeZoneInfo Sydney = ScheduleClock.Resolve("Australia/Sydney");

    private static BackupSettings Daily(int hour, int minute = 0) => new()
    {
        AutoBackupEnabled = true,
        Cadence = BackupCadence.Daily,
        Hour = hour,
        Minute = minute
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_run_in_utc_is_the_next_occurrence_of_the_wall_clock_time()
    {
        var next = BackupScheduleEvaluator.GetNextRunUtc(Daily(3), Utc(2026, 10, 5, 12), TimeZoneInfo.Utc);

        next.Should().Be(Utc(2026, 10, 6, 3));
    }

    [Fact]
    public void Daily_run_follows_the_server_time_zone_rather_than_utc()
    {
        var next = BackupScheduleEvaluator.GetNextRunUtc(Daily(3), Utc(2026, 10, 5, 12), Chicago);

        next.Should().Be(Utc(2026, 10, 6, 8));
    }

    [Fact]
    public void A_time_still_ahead_today_in_the_server_zone_runs_today()
    {
        var next = BackupScheduleEvaluator.GetNextRunUtc(Daily(22), Utc(2026, 10, 5, 12), Chicago);

        next.Should().Be(Utc(2026, 10, 6, 3));
    }

    [Fact]
    public void Weekly_day_of_week_is_read_in_the_server_zone()
    {
        var settings = new BackupSettings
        {
            AutoBackupEnabled = true,
            Cadence = BackupCadence.Weekly,
            DayOfWeek = DayOfWeek.Monday,
            Hour = 3
        };

        var next = BackupScheduleEvaluator.GetNextRunUtc(settings, Utc(2026, 10, 4, 15), Sydney);

        next.Should().Be(Utc(2026, 10, 4, 16));
    }

    [Fact]
    public void Monthly_run_moves_to_next_month_once_this_months_day_has_passed()
    {
        var settings = new BackupSettings
        {
            AutoBackupEnabled = true,
            Cadence = BackupCadence.Monthly,
            DayOfMonth = 1,
            Hour = 3
        };

        var next = BackupScheduleEvaluator.GetNextRunUtc(settings, Utc(2026, 10, 5, 12), Chicago);

        next.Should().Be(Utc(2026, 11, 1, 9));
    }

    [Fact]
    public void A_time_skipped_by_the_spring_forward_change_runs_when_the_clock_jumps()
    {
        var next = BackupScheduleEvaluator.GetNextRunUtc(Daily(2, 30), Utc(2026, 3, 8, 6), Chicago);

        next.Should().Be(Utc(2026, 3, 8, 8));
    }

    [Fact]
    public void Daylight_saving_time_keeps_the_wall_clock_time_after_the_change()
    {
        var beforeChange = BackupScheduleEvaluator.GetNextRunUtc(Daily(3), Utc(2026, 3, 6, 12), Chicago);
        var afterChange = BackupScheduleEvaluator.GetNextRunUtc(Daily(3), Utc(2026, 3, 8, 12), Chicago);

        beforeChange.Should().Be(Utc(2026, 3, 7, 9));
        afterChange.Should().Be(Utc(2026, 3, 9, 8));
    }

    [Fact]
    public void A_repeated_hour_in_the_fall_back_change_runs_once()
    {
        var settings = Daily(1, 30);

        var first = BackupScheduleEvaluator.GetNextRunUtc(settings, Utc(2026, 11, 1, 4), Chicago);
        first.Should().NotBeNull();
        var second = BackupScheduleEvaluator.GetNextRunUtc(settings, first ?? DateTime.MinValue, Chicago);

        first.Should().Be(Utc(2026, 11, 1, 7, 30));
        second.Should().Be(Utc(2026, 11, 2, 7, 30));
    }

    [Fact]
    public void IsDue_compares_against_the_run_after_the_last_backup_in_the_server_zone()
    {
        var settings = Daily(3);
        settings.LastSuccessfulRunUtc = Utc(2026, 10, 5, 8, 1);

        BackupScheduleEvaluator.IsDue(settings, Utc(2026, 10, 6, 7, 59), Chicago).Should().BeFalse();
        BackupScheduleEvaluator.IsDue(settings, Utc(2026, 10, 6, 8), Chicago).Should().BeTrue();
        BackupScheduleEvaluator.IsDue(settings, Utc(2026, 10, 6, 3), TimeZoneInfo.Utc).Should().BeTrue();
    }

    [Fact]
    public void A_schedule_that_never_ran_is_due_immediately()
    {
        BackupScheduleEvaluator.IsDue(Daily(3), Utc(2026, 10, 5, 12), Chicago).Should().BeTrue();
    }

    [Fact]
    public void A_disabled_schedule_has_no_next_run_and_is_never_due()
    {
        var off = new BackupSettings { AutoBackupEnabled = true, Cadence = BackupCadence.Off };
        var disabled = new BackupSettings { AutoBackupEnabled = false, Cadence = BackupCadence.Daily };

        BackupScheduleEvaluator.GetNextRunUtc(off, Utc(2026, 10, 5, 12), Chicago).Should().BeNull();
        BackupScheduleEvaluator.IsDue(disabled, Utc(2026, 10, 5, 12), Chicago).Should().BeFalse();
        BackupScheduleEvaluator.GetDisplayedNextRunUtc(disabled, Utc(2026, 10, 5, 12), Chicago).Should().BeNull();
    }

    [Fact]
    public void Displayed_next_run_is_the_upcoming_slot_or_now_when_a_run_is_overdue()
    {
        var now = Utc(2026, 10, 5, 12);
        var settings = Daily(3);

        BackupScheduleEvaluator.GetDisplayedNextRunUtc(settings, now, Chicago).Should().Be(now);

        settings.LastSuccessfulRunUtc = Utc(2026, 10, 5, 8, 1);
        BackupScheduleEvaluator.GetDisplayedNextRunUtc(settings, now, Chicago).Should().Be(Utc(2026, 10, 6, 8));

        settings.LastSuccessfulRunUtc = Utc(2026, 9, 1, 8, 1);
        BackupScheduleEvaluator.GetDisplayedNextRunUtc(settings, now, Chicago).Should().Be(now);
    }
}
