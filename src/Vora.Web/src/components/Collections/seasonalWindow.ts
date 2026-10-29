import { daysInMonth } from '../../utils/monthDay';

// A collection shows between two dates, or between two days of every year. A
// window that repeats is stored as dates in 2000 (a leap year, so Feb 29 is
// allowed); the server reads only their month and day.
const YEARLESS = 2000;

export interface SeasonalWindow {
    everyYear: boolean;
    startDate: string;
    endDate: string;
    startMonth: number;
    startDay: number;
    endMonth: number;
    endDay: number;
}

export interface SeasonalWindowRequest {
    visibleStartDate?: string;
    visibleEndDate?: string;
    visibleEveryYear: boolean;
}

const pad = (value: number) => String(value).padStart(2, '0');

const dateInput = (iso?: string | null): string => (iso ? new Date(iso).toISOString().split('T')[0] : '');

const monthAndDay = (date: string): [number, number] | null => {
    const [, month, day] = date.split('-').map(Number);
    return month && day ? [month, day] : null;
};

export function seasonalWindowFrom(start?: string | null, end?: string | null, everyYear = false, today = new Date()): SeasonalWindow {
    const startDate = dateInput(start);
    const endDate = dateInput(end);
    const month = today.getMonth() + 1;
    const [startMonth, startDay] = monthAndDay(startDate) ?? [month, 1];
    const [endMonth, endDay] = monthAndDay(endDate) ?? [month, daysInMonth(month)];

    return {
        everyYear,
        startDate: everyYear ? '' : startDate,
        endDate: everyYear ? '' : endDate,
        startMonth,
        startDay,
        endMonth,
        endDay,
    };
}

export function withEveryYear(window: SeasonalWindow, everyYear: boolean): SeasonalWindow {
    if (!everyYear) return { ...window, everyYear };

    const start = monthAndDay(window.startDate);
    const end = monthAndDay(window.endDate);
    return {
        ...window,
        everyYear,
        startMonth: start?.[0] ?? window.startMonth,
        startDay: start?.[1] ?? window.startDay,
        endMonth: end?.[0] ?? window.endMonth,
        endDay: end?.[1] ?? window.endDay,
    };
}

export function seasonalWindowRequest(window: SeasonalWindow): SeasonalWindowRequest {
    if (!window.everyYear) {
        return {
            visibleStartDate: window.startDate || undefined,
            visibleEndDate: window.endDate || undefined,
            visibleEveryYear: false,
        };
    }

    const yearless = (month: number, day: number) => `${YEARLESS}-${pad(month)}-${pad(Math.min(day, daysInMonth(month)))}`;
    return {
        visibleStartDate: yearless(window.startMonth, window.startDay),
        visibleEndDate: yearless(window.endMonth, window.endDay),
        visibleEveryYear: true,
    };
}
