// Turning what the server sends into what the viewer sees.
//
// The server stores and emits UTC. A timestamp that arrives without a zone
// ("2026-09-18T20:30:00") is still UTC — but `new Date(...)` reads exactly that
// shape as LOCAL time, so every bare parse silently shifted by the viewer's
// offset. Everything that renders a server timestamp goes through here.
//
// Date-only values are a different kind of thing. A release date, an air date
// or a birthday is a calendar date, not an instant: it must read the same in
// Auckland and in Los Angeles. Those never get timezone maths applied.

const DateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const DateTimePrefixPattern = /^(\d{4})-(\d{2})-(\d{2})T/;
const HasZonePattern = /(?:Z|[+-]\d{2}:?\d{2})$/i;

export function isDateOnly(value?: string | null): boolean {
    return !!value && DateOnlyPattern.test(value.trim());
}

// The instant a server timestamp refers to. A date-only value becomes local
// midnight of that date, so it lands on the day it names.
export function parseServerDate(value?: string | null): Date | null {
    if (!value) return null;
    const raw = value.trim();
    if (!raw) return null;

    const dateOnly = DateOnlyPattern.exec(raw);
    if (dateOnly) {
        return new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]));
    }

    const parsed = new Date(HasZonePattern.test(raw) ? raw : `${raw}Z`);
    return isNaN(parsed.getTime()) ? null : parsed;
}

// Milliseconds since the epoch, for sorting and comparing. Unparseable values
// sort last rather than poisoning the comparison with NaN.
export function serverTimeMs(value?: string | null): number {
    return parseServerDate(value)?.getTime() ?? 0;
}

export function formatDateTime(value?: string | null, options?: Intl.DateTimeFormatOptions): string {
    const date = parseServerDate(value);
    return date ? date.toLocaleString(undefined, options) : '';
}

export function formatTime(value?: string | null, options: Intl.DateTimeFormatOptions = { hour: 'numeric', minute: '2-digit' }): string {
    const date = parseServerDate(value);
    return date ? date.toLocaleTimeString(undefined, options) : '';
}

export function formatDate(value?: string | null, options?: Intl.DateTimeFormatOptions): string {
    const date = parseServerDate(value);
    return date ? date.toLocaleDateString(undefined, options) : '';
}

// The year a release date names, read off the string. Parsing "2026-01-01" and
// asking for getFullYear() answers 2025 anywhere west of Greenwich.
export function yearOf(value?: string | null): number | null {
    if (!value) return null;
    const match = DateOnlyPattern.exec(value.trim()) ?? DateTimePrefixPattern.exec(value.trim());
    return match ? Number(match[1]) : null;
}

// Whether two instants fall on the same day in the viewer's zone.
export function isSameLocalDay(a: Date | null, b: Date | null): boolean {
    if (!a || !b) return false;
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

// Whole days between two instants by the calendar the viewer lives in, not by
// elapsed milliseconds: 23:00 yesterday to 02:00 today is one day, not zero.
export function calendarDaysBetween(from: Date, to: Date): number {
    const fromMidnight = new Date(from.getFullYear(), from.getMonth(), from.getDate()).getTime();
    const toMidnight = new Date(to.getFullYear(), to.getMonth(), to.getDate()).getTime();
    return Math.round((toMidnight - fromMidnight) / 86_400_000);
}
