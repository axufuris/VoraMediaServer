using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

// A container has no time zone unless it is given one, so every scheduled time
// an admin typed was being compared against UTC. On a UTC-5 server "02:00"
// therefore fired at 9pm the previous evening — which is how a nightly scan can
// look like it never ran.
public class ScheduleClockTests
{
    private static readonly DateTime WinterUtc = new(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SummerUtc = new(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_configured_zone_shifts_the_clock_off_utc()
    {
        var now = ScheduleClock.Now("America/Chicago", WinterUtc);

        now.Hour.Should().Be(2);
        now.Date.Should().Be(new DateTime(2026, 1, 15));
    }

    // The offset has to come from the zone, not a stored number, or every
    // schedule drifts by an hour twice a year.
    [Fact]
    public void Daylight_saving_is_taken_from_the_zone()
    {
        ScheduleClock.Now("America/Chicago", WinterUtc).Hour.Should().Be(2);
        ScheduleClock.Now("America/Chicago", SummerUtc).Hour.Should().Be(3);
    }

    // Crossing midnight is the case that decides which DAY a job counts as
    // having run on, which is what stops it firing twice or not at all.
    [Fact]
    public void A_zone_behind_utc_can_still_be_on_the_previous_day()
    {
        var now = ScheduleClock.Now("America/Chicago", new DateTime(2026, 1, 15, 3, 0, 0, DateTimeKind.Utc));

        now.Date.Should().Be(new DateTime(2026, 1, 14));
        now.Hour.Should().Be(21);
    }

    [Fact]
    public void An_empty_setting_falls_back_to_the_container_clock()
    {
        var now = ScheduleClock.Now("", WinterUtc);

        now.Should().Be(TimeZoneInfo.ConvertTimeFromUtc(WinterUtc, TimeZoneInfo.Local));
    }

    // A typo must not take the scheduler down or silently stop every job; it
    // falls back to the previous behaviour instead.
    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("America/Chicagoo")]
    public void An_unknown_zone_falls_back_rather_than_throwing(string bogus)
    {
        var act = () => ScheduleClock.Now(bogus, WinterUtc);

        act.Should().NotThrow();
        ScheduleClock.Now(bogus, WinterUtc).Should().Be(TimeZoneInfo.ConvertTimeFromUtc(WinterUtc, TimeZoneInfo.Local));
    }

    [Theory]
    [InlineData("America/Chicago", true)]
    [InlineData("UTC", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("Not/AZone", false)]
    public void Only_a_resolvable_id_is_accepted(string? id, bool expected)
    {
        ScheduleClock.IsKnown(id).Should().Be(expected);
    }

    [Fact]
    public void Surrounding_whitespace_does_not_make_a_valid_zone_unknown()
    {
        ScheduleClock.IsKnown("  America/Chicago  ").Should().BeTrue();
        ScheduleClock.Now("  America/Chicago  ", WinterUtc).Hour.Should().Be(2);
    }
}
