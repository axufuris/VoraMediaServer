import { describe, it, expect } from 'vitest';
import {
    calendarDaysBetween,
    formatDate,
    formatTime,
    isDateOnly,
    isSameLocalDay,
    parseServerDate,
    serverTimeMs,
    yearOf,
} from './serverTime';

describe('parseServerDate', () => {
    it('reads a zone-less timestamp as UTC, the way the server meant it', () => {
        expect(parseServerDate('2026-09-18T20:30:00')?.toISOString()).toBe('2026-09-18T20:30:00.000Z');
    });

    it('reads an explicit Z as UTC', () => {
        expect(parseServerDate('2026-09-18T20:30:00Z')?.toISOString()).toBe('2026-09-18T20:30:00.000Z');
        expect(parseServerDate('2026-09-18T20:30:00.1234567Z')?.toISOString()).toBe('2026-09-18T20:30:00.123Z');
    });

    it('honours an offset that is already on the string', () => {
        expect(parseServerDate('2026-09-18T20:30:00+02:00')?.toISOString()).toBe('2026-09-18T18:30:00.000Z');
        expect(parseServerDate('2026-09-18T20:30:00-05:00')?.toISOString()).toBe('2026-09-19T01:30:00.000Z');
    });

    it('puts a date-only value on the day it names, in any zone', () => {
        const date = parseServerDate('2026-01-01');

        expect(date?.getFullYear()).toBe(2026);
        expect(date?.getMonth()).toBe(0);
        expect(date?.getDate()).toBe(1);
        expect(date?.getHours()).toBe(0);
    });

    it('has nothing to say about nothing', () => {
        expect(parseServerDate(null)).toBeNull();
        expect(parseServerDate(undefined)).toBeNull();
        expect(parseServerDate('')).toBeNull();
        expect(parseServerDate('   ')).toBeNull();
        expect(parseServerDate('not a date')).toBeNull();
    });
});

describe('isDateOnly', () => {
    it('separates a calendar date from an instant', () => {
        expect(isDateOnly('2026-01-01')).toBe(true);
        expect(isDateOnly('2026-01-01T00:00:00Z')).toBe(false);
        expect(isDateOnly(null)).toBe(false);
    });
});

describe('serverTimeMs', () => {
    it('gives zone-less and Z forms the same instant', () => {
        expect(serverTimeMs('2026-09-18T20:30:00')).toBe(serverTimeMs('2026-09-18T20:30:00Z'));
    });

    it('sorts unreadable values to the start rather than returning NaN', () => {
        expect(serverTimeMs('nonsense')).toBe(0);
        expect(serverTimeMs(null)).toBe(0);
    });
});

describe('yearOf', () => {
    it('reads the year off the string, never through a timezone', () => {
        expect(yearOf('2026-01-01')).toBe(2026);
        expect(yearOf('2026-01-01T00:00:00Z')).toBe(2026);
        expect(yearOf('1999-12-31T23:59:59')).toBe(1999);
    });

    it('has no year for nothing', () => {
        expect(yearOf(null)).toBeNull();
        expect(yearOf('')).toBeNull();
        expect(yearOf('sometime')).toBeNull();
    });
});

describe('formatting', () => {
    it('renders a zone-less instant in the viewer\'s zone', () => {
        const utc = parseServerDate('2026-09-18T20:30:00');
        const expected = utc!.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

        expect(formatTime('2026-09-18T20:30:00')).toBe(expected);
        expect(formatTime('2026-09-18T20:30:00Z')).toBe(expected);
    });

    it('renders a date-only value as that date', () => {
        expect(formatDate('2026-01-01')).toBe(new Date(2026, 0, 1).toLocaleDateString());
    });

    it('renders nothing for a missing value', () => {
        expect(formatTime(null)).toBe('');
        expect(formatDate(undefined)).toBe('');
    });
});

describe('local-day helpers', () => {
    it('matches two instants on the same local day', () => {
        expect(isSameLocalDay(new Date(2026, 8, 18, 1, 0), new Date(2026, 8, 18, 23, 0))).toBe(true);
        expect(isSameLocalDay(new Date(2026, 8, 18, 23, 0), new Date(2026, 8, 19, 1, 0))).toBe(false);
        expect(isSameLocalDay(null, new Date())).toBe(false);
    });

    it('counts calendar days, not elapsed hours', () => {
        expect(calendarDaysBetween(new Date(2026, 8, 17, 23, 0), new Date(2026, 8, 18, 2, 0))).toBe(1);
        expect(calendarDaysBetween(new Date(2026, 8, 18, 0, 5), new Date(2026, 8, 18, 23, 55))).toBe(0);
        expect(calendarDaysBetween(new Date(2026, 8, 11, 12, 0), new Date(2026, 8, 18, 12, 0))).toBe(7);
    });
});
