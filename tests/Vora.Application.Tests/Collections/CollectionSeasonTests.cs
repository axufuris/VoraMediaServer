using Vora.Application.Collections;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Tests.Collections;

public class CollectionSeasonTests
{
    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    private static DateOnly Today(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void A_collection_without_dates_is_always_open()
    {
        CollectionSeason.IsOpen(null, null, everyYear: false, Today(2026, 6, 1)).Should().BeTrue();
        CollectionSeason.IsOpen(null, null, everyYear: true, Today(2026, 6, 1)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2026, 9, 30, false)]
    [InlineData(2026, 10, 1, true)]
    [InlineData(2026, 10, 31, true)]
    [InlineData(2026, 11, 1, false)]
    [InlineData(2027, 10, 15, false)]
    public void A_dated_window_is_open_from_its_first_day_through_its_last(int year, int month, int day, bool open)
    {
        CollectionSeason.IsOpen(Day(2026, 10, 1), Day(2026, 10, 31), everyYear: false, Today(year, month, day)).Should().Be(open);
    }

    [Theory]
    [InlineData(2026, 10, 15, true)]
    [InlineData(2027, 10, 1, true)]
    [InlineData(2031, 10, 31, true)]
    [InlineData(2027, 11, 1, false)]
    [InlineData(2027, 9, 30, false)]
    public void A_window_that_repeats_opens_on_the_same_days_every_year(int year, int month, int day, bool open)
    {
        CollectionSeason.IsOpen(Day(2026, 10, 1), Day(2026, 10, 31), everyYear: true, Today(year, month, day)).Should().Be(open);
    }

    [Theory]
    [InlineData(12, 20, true)]
    [InlineData(12, 31, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 6, true)]
    [InlineData(1, 7, false)]
    [InlineData(12, 19, false)]
    [InlineData(6, 1, false)]
    public void A_repeating_window_can_run_over_new_year(int month, int day, bool open)
    {
        CollectionSeason.IsOpen(Day(2000, 12, 20), Day(2000, 1, 6), everyYear: true, Today(2027, month, day)).Should().Be(open);
    }

    [Fact]
    public void A_repeating_window_with_one_side_runs_to_the_end_or_from_the_start_of_the_year()
    {
        CollectionSeason.IsOpen(Day(2000, 11, 1), null, everyYear: true, Today(2026, 12, 31)).Should().BeTrue();
        CollectionSeason.IsOpen(Day(2000, 11, 1), null, everyYear: true, Today(2026, 10, 31)).Should().BeFalse();
        CollectionSeason.IsOpen(null, Day(2000, 2, 14), everyYear: true, Today(2026, 1, 1)).Should().BeTrue();
        CollectionSeason.IsOpen(null, Day(2000, 2, 14), everyYear: true, Today(2026, 2, 15)).Should().BeFalse();
    }

    [Fact]
    public void The_query_filter_agrees_with_the_rule_on_every_day_of_the_year()
    {
        var collections = new List<Collection>
        {
            new() { Title = "Always" },
            new() { Title = "October 2026", VisibleStartDate = Day(2026, 10, 1), VisibleEndDate = Day(2026, 10, 31) },
            new() { Title = "Every October", VisibleStartDate = Day(2000, 10, 1), VisibleEndDate = Day(2000, 10, 31), VisibleEveryYear = true },
            new() { Title = "Every holiday season", VisibleStartDate = Day(2000, 12, 20), VisibleEndDate = Day(2000, 1, 6), VisibleEveryYear = true },
            new() { Title = "From June", VisibleStartDate = Day(2026, 6, 1) },
            new() { Title = "Every spring from March", VisibleStartDate = Day(2000, 3, 1), VisibleEveryYear = true },
            new() { Title = "Every leap day", VisibleStartDate = Day(2000, 2, 29), VisibleEndDate = Day(2000, 2, 29), VisibleEveryYear = true },
        };

        for (var today = Today(2026, 1, 1); today <= Today(2028, 12, 31); today = today.AddDays(1))
        {
            var expected = collections
                .Where(c => CollectionSeason.IsOpen(c.VisibleStartDate, c.VisibleEndDate, c.VisibleEveryYear, today))
                .Select(c => c.Title);

            collections.AsQueryable().InSeason(today).Select(c => c.Title).Should().Equal(expected, $"on {today}");
        }
    }
}
