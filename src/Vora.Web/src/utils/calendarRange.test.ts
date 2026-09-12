import { describe, it, expect } from 'vitest';
import {
    daysInRange,
    isCalendarViewMode,
    rangeTitle,
    step,
    visibleRange,
} from './calendarRange';

// Local dates on purpose: the calendar is about the day a release lands where
// the viewer is, and building from UTC parts shifts events across midnight for
// anyone west of Greenwich.
const on = (year: number, month1: number, day: number) => new Date(year, month1 - 1, day);
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

describe('visible range', () => {
    // The month grid is whole weeks, so it reaches back into the previous month
    // and on into the next — which is also what gets fetched.
    it('runs a month from the Sunday before the 1st to the Saturday after the last', () => {
        const { start, end } = visibleRange('month', on(2026, 9, 10));

        expect(iso(start)).toBe('2026-08-30');
        expect(iso(end)).toBe('2026-10-03');
    });

    it('covers a month in whole weeks', () => {
        const { start, end } = visibleRange('month', on(2026, 9, 10));

        expect(daysInRange(start, end).length % 7).toBe(0);
    });

    it('runs a week Sunday to Saturday around the anchor', () => {
        const { start, end } = visibleRange('week', on(2026, 9, 10));

        expect(iso(start)).toBe('2026-09-06');
        expect(iso(end)).toBe('2026-09-12');
        expect(daysInRange(start, end)).toHaveLength(7);
    });

    it('keeps a Sunday anchor in its own week', () => {
        expect(iso(visibleRange('week', on(2026, 9, 6)).start)).toBe('2026-09-06');
    });

    it('runs a day from and to itself', () => {
        const { start, end } = visibleRange('day', on(2026, 9, 10));

        expect(iso(start)).toBe('2026-09-10');
        expect(iso(end)).toBe('2026-09-10');
        expect(daysInRange(start, end)).toHaveLength(1);
    });

    // A month that starts on a Sunday needs no lead-in, which is the case most
    // likely to produce an extra blank week.
    it('adds no leading week when the month already starts on a Sunday', () => {
        expect(iso(visibleRange('month', on(2026, 11, 15)).start)).toBe('2026-11-01');
    });
});

describe('stepping', () => {
    it('moves a month at a time in month view', () => {
        expect(iso(step('month', on(2026, 9, 10), 1))).toBe('2026-10-01');
        expect(iso(step('month', on(2026, 9, 10), -1))).toBe('2026-08-01');
    });

    it('moves a week at a time in week view', () => {
        expect(iso(step('week', on(2026, 9, 10), 1))).toBe('2026-09-17');
        expect(iso(step('week', on(2026, 9, 10), -1))).toBe('2026-09-03');
    });

    it('moves a day at a time in day view', () => {
        expect(iso(step('day', on(2026, 9, 10), 1))).toBe('2026-09-11');
        expect(iso(step('day', on(2026, 9, 10), -1))).toBe('2026-09-09');
    });

    it('crosses a year boundary', () => {
        expect(iso(step('day', on(2026, 12, 31), 1))).toBe('2027-01-01');
        expect(iso(step('month', on(2026, 12, 15), 1))).toBe('2027-01-01');
    });

    // Stepping from the 31st into a shorter month must not roll into the one
    // after it.
    it('does not overshoot a shorter month', () => {
        expect(iso(step('month', on(2026, 1, 31), 1))).toBe('2026-02-01');
    });
});

describe('range titles', () => {
    it('names the month', () => {
        expect(rangeTitle('month', on(2026, 9, 10))).toContain('2026');
        expect(rangeTitle('month', on(2026, 9, 10))).toMatch(/September/);
    });

    it('shows only the closing day when a week sits inside one month', () => {
        expect(rangeTitle('week', on(2026, 9, 10))).toBe('Sep 6 – 12');
    });

    // A week straddling two months has to name both, or "Sep 28 – 4" reads as
    // going backwards.
    it('names both months when a week straddles them', () => {
        expect(rangeTitle('week', on(2026, 9, 30))).toBe('Sep 27 – Oct 3');
    });

    it('names the year on a week that crosses one', () => {
        expect(rangeTitle('week', on(2026, 12, 31))).toMatch(/2026.*2027/);
    });

    it('names the weekday and date for a day', () => {
        expect(rangeTitle('day', on(2026, 9, 10))).toBe('Thu, Sep 10, 2026');
    });
});

describe('stored view mode', () => {
    it.each(['month', 'week', 'day'])('accepts %s', mode => {
        expect(isCalendarViewMode(mode)).toBe(true);
    });

    // A stale or hand-edited value must fall back rather than render nothing.
    it.each([null, undefined, '', 'year', 42])('rejects %s', value => {
        expect(isCalendarViewMode(value)).toBe(false);
    });
});
