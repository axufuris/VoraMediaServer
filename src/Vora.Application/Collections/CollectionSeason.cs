using Vora.Domain.Entities.Library;

namespace Vora.Application.Collections;

public static class CollectionSeason
{
    private const int FirstDay = 101;
    private const int LastDay = 1231;

    public static bool IsOpen(DateTime? start, DateTime? end, bool everyYear, DateOnly today)
    {
        if (start == null && end == null) return true;

        if (!everyYear)
        {
            return (start == null || DateOnly.FromDateTime(start.Value) <= today)
                && (end == null || DateOnly.FromDateTime(end.Value) >= today);
        }

        var from = start == null ? FirstDay : start.Value.Month * 100 + start.Value.Day;
        var to = end == null ? LastDay : end.Value.Month * 100 + end.Value.Day;
        var now = today.Month * 100 + today.Day;

        return from <= to ? now >= from && now <= to : now >= from || now <= to;
    }

    public static IQueryable<Collection> InSeason(this IQueryable<Collection> collections, DateOnly today)
    {
        var date = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var now = today.Month * 100 + today.Day;

        return collections
            .Select(c => new
            {
                Collection = c,
                Dated = c.VisibleStartDate != null || c.VisibleEndDate != null,
                From = c.VisibleStartDate == null ? FirstDay : c.VisibleStartDate.Value.Month * 100 + c.VisibleStartDate.Value.Day,
                To = c.VisibleEndDate == null ? LastDay : c.VisibleEndDate.Value.Month * 100 + c.VisibleEndDate.Value.Day,
            })
            .Where(w => !w.Dated
                || (!w.Collection.VisibleEveryYear
                    && (w.Collection.VisibleStartDate == null || w.Collection.VisibleStartDate.Value.Date <= date)
                    && (w.Collection.VisibleEndDate == null || w.Collection.VisibleEndDate.Value.Date >= date))
                || (w.Collection.VisibleEveryYear
                    && (w.From <= w.To ? now >= w.From && now <= w.To : now >= w.From || now <= w.To)))
            .Select(w => w.Collection);
    }
}
