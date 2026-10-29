import { describe, it, expect } from 'vitest';
import { seasonalWindowFrom, seasonalWindowRequest, withEveryYear } from './seasonalWindow';

const october = new Date(2026, 9, 12);

describe('seasonalWindow', () => {
    it('reads a dated window into its date fields', () => {
        const window = seasonalWindowFrom('2026-10-01T00:00:00Z', '2026-10-31T00:00:00Z', false, october);

        expect(window).toMatchObject({ everyYear: false, startDate: '2026-10-01', endDate: '2026-10-31', startMonth: 10, startDay: 1, endMonth: 10, endDay: 31 });
        expect(seasonalWindowRequest(window)).toEqual({ visibleStartDate: '2026-10-01', visibleEndDate: '2026-10-31', visibleEveryYear: false });
    });

    it('reads a repeating window into its month and day, and sends it back in 2000', () => {
        const window = seasonalWindowFrom('2000-12-20T00:00:00Z', '2000-01-06T00:00:00Z', true, october);

        expect(window).toMatchObject({ everyYear: true, startDate: '', endDate: '', startMonth: 12, startDay: 20, endMonth: 1, endDay: 6 });
        expect(seasonalWindowRequest(window)).toEqual({ visibleStartDate: '2000-12-20', visibleEndDate: '2000-01-06', visibleEveryYear: true });
    });

    it('starts a new window on this month, and a blank dated one sends nothing', () => {
        const window = seasonalWindowFrom(undefined, undefined, false, october);

        expect(window).toMatchObject({ startMonth: 10, startDay: 1, endMonth: 10, endDay: 31 });
        expect(seasonalWindowRequest(window)).toEqual({ visibleStartDate: undefined, visibleEndDate: undefined, visibleEveryYear: false });
    });

    it('keeps the days of the dates typed when the window is made to repeat', () => {
        const dated = { ...seasonalWindowFrom(undefined, undefined, false, october), startDate: '2026-02-01', endDate: '2026-02-14' };

        expect(withEveryYear(dated, true)).toMatchObject({ everyYear: true, startMonth: 2, startDay: 1, endMonth: 2, endDay: 14 });
        expect(withEveryYear(dated, false)).toMatchObject({ everyYear: false, startDate: '2026-02-01', endDate: '2026-02-14' });
    });

    it('lets a repeating window start on February 29', () => {
        const window = { ...seasonalWindowFrom(undefined, undefined, true, october), startMonth: 2, startDay: 29, endMonth: 3, endDay: 1 };

        expect(seasonalWindowRequest(window)).toMatchObject({ visibleStartDate: '2000-02-29', visibleEndDate: '2000-03-01' });
    });
});
