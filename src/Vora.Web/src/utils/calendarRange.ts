// Date maths for the release calendar's three views.
//
// Kept apart from the page because every one of these is a pure function of
// (mode, anchor) and the off-by-one cases — a week that straddles two months, a
// month grid that has to start on a Sunday — are exactly the kind of thing worth
// pinning down in tests rather than eyeballing in a grid.
//
// Everything works in LOCAL time. The calendar is about the day a release lands
// where the viewer is, so constructing dates from UTC parts would shift events
// across midnight for anyone west of Greenwich.

export type CalendarViewMode = 'month' | 'week' | 'day';

export const CalendarViewModes: readonly CalendarViewMode[] = ['month', 'week', 'day'];

export function isCalendarViewMode(value: unknown): value is CalendarViewMode {
    return typeof value === 'string' && (CalendarViewModes as readonly string[]).includes(value);
}

function startOfDay(date: Date): Date {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addDays(date: Date, days: number): Date {
    const next = startOfDay(date);
    next.setDate(next.getDate() + days);
    return next;
}

function startOfWeek(date: Date): Date {
    return addDays(date, -startOfDay(date).getDay());
}

// The inclusive span of days the view shows, which is also what gets asked of
// the server — a week view fetches a week, not the whole month around it.
export function visibleRange(mode: CalendarViewMode, anchor: Date): { start: Date; end: Date } {
    if (mode === 'day') {
        const day = startOfDay(anchor);
        return { start: day, end: day };
    }

    if (mode === 'week') {
        const start = startOfWeek(anchor);
        return { start, end: addDays(start, 6) };
    }

    // A month grid is whole weeks: back to the Sunday on or before the 1st, on
    // to the Saturday on or after the last day.
    const firstOfMonth = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
    const lastOfMonth = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0);

    return { start: startOfWeek(firstOfMonth), end: addDays(lastOfMonth, 6 - lastOfMonth.getDay()) };
}

export function daysInRange(start: Date, end: Date): Date[] {
    const days: Date[] = [];
    for (let day = startOfDay(start); day <= end; day = addDays(day, 1)) {
        days.push(day);
    }
    return days;
}

// Prev/Next move by whatever the active view shows, so Next in week view is the
// next week rather than the next month.
export function step(mode: CalendarViewMode, anchor: Date, direction: 1 | -1): Date {
    if (mode === 'day') return addDays(anchor, direction);
    if (mode === 'week') return addDays(anchor, 7 * direction);

    return new Date(anchor.getFullYear(), anchor.getMonth() + direction, 1);
}

export function rangeTitle(mode: CalendarViewMode, anchor: Date): string {
    if (mode === 'day') {
        return anchor.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
    }

    if (mode === 'week') {
        const { start, end } = visibleRange('week', anchor);
        const sameMonth = start.getMonth() === end.getMonth() && start.getFullYear() === end.getFullYear();
        const sameYear = start.getFullYear() === end.getFullYear();

        const from = start.toLocaleDateString(undefined, sameYear
            ? { month: 'short', day: 'numeric' }
            : { month: 'short', day: 'numeric', year: 'numeric' });

        // "Sep 7 – 13" when the week sits in one month, "Sep 28 – Oct 4" when it
        // straddles two, and the year only when it changes.
        const to = end.toLocaleDateString(undefined, sameMonth
            ? { day: 'numeric' }
            : sameYear
                ? { month: 'short', day: 'numeric' }
                : { month: 'short', day: 'numeric', year: 'numeric' });

        return `${from} – ${to}`;
    }

    return anchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
}
