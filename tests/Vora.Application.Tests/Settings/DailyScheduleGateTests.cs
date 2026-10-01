using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

public class DailyScheduleGateTests
{
    private static readonly TimeSpan TwoAm = new(2, 0, 0);
    private static readonly TimeSpan TenPastEleven = new(11, 10, 0);

    private static DateTime At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void A_time_already_passed_at_startup_waits_for_tomorrow()
    {
        var gate = new DailyScheduleGate();

        gate.IsDue(TwoAm, At(1, 9, 0)).Should().BeFalse();
        gate.IsDue(TwoAm, At(2, 2, 0)).Should().BeTrue();
    }

    [Fact]
    public void A_time_still_ahead_at_startup_fires_today()
    {
        var gate = new DailyScheduleGate();

        gate.IsDue(TwoAm, At(1, 1, 0)).Should().BeFalse();
        gate.IsDue(TwoAm, At(1, 2, 5)).Should().BeTrue();
    }

    [Fact]
    public void It_fires_once_per_day()
    {
        var gate = new DailyScheduleGate();
        gate.IsDue(TwoAm, At(1, 1, 0));

        gate.IsDue(TwoAm, At(1, 2, 5)).Should().BeTrue();
        gate.MarkRan(At(1, 2, 5));

        gate.IsDue(TwoAm, At(1, 2, 10)).Should().BeFalse();
        gate.IsDue(TwoAm, At(1, 23, 55)).Should().BeFalse();
        gate.IsDue(TwoAm, At(2, 2, 0)).Should().BeTrue();
    }

    [Fact]
    public void Moving_the_time_later_in_the_day_fires_at_the_new_time_today()
    {
        var gate = new DailyScheduleGate();
        gate.IsDue(TwoAm, At(1, 9, 0)).Should().BeFalse();

        gate.IsDue(TenPastEleven, At(1, 11, 0)).Should().BeFalse();
        gate.IsDue(TenPastEleven, At(1, 11, 10)).Should().BeTrue();
    }

    [Fact]
    public void Moving_the_time_later_after_today_already_ran_still_fires_at_the_new_time()
    {
        var gate = new DailyScheduleGate();
        gate.IsDue(TwoAm, At(1, 1, 0));
        gate.IsDue(TwoAm, At(1, 2, 0)).Should().BeTrue();
        gate.MarkRan(At(1, 2, 0));

        gate.IsDue(TenPastEleven, At(1, 11, 5)).Should().BeFalse();
        gate.IsDue(TenPastEleven, At(1, 11, 15)).Should().BeTrue();
    }

    [Fact]
    public void Moving_the_time_to_one_already_passed_waits_for_tomorrow()
    {
        var gate = new DailyScheduleGate();
        gate.IsDue(TenPastEleven, At(1, 9, 0)).Should().BeFalse();

        gate.IsDue(TwoAm, At(1, 9, 5)).Should().BeFalse();
        gate.IsDue(TwoAm, At(1, 23, 55)).Should().BeFalse();
        gate.IsDue(TwoAm, At(2, 2, 0)).Should().BeTrue();
    }
}
