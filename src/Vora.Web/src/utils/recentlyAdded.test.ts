import { describe, it, expect } from 'vitest';
import { recentlyAddedTime } from './recentlyAdded';

describe('recently added time', () => {
    // A season created in August that gained an episode today sorts as today.
    it('uses when the newest content arrived', () => {
        expect(recentlyAddedTime({ addedAt: '2026-08-10T23:17:09Z', lastContentAddedAt: '2026-09-14T20:08:21Z' }))
            .toBe(Date.parse('2026-09-14T20:08:21Z'));
    });

    it('falls back to when the item was added', () => {
        expect(recentlyAddedTime({ addedAt: '2026-08-10T23:17:09Z', lastContentAddedAt: null }))
            .toBe(Date.parse('2026-08-10T23:17:09Z'));
    });

    it('treats a missing date as the oldest', () => {
        expect(recentlyAddedTime({})).toBe(0);
    });

    it('ignores an unreadable date', () => {
        expect(recentlyAddedTime({ addedAt: '2026-08-10T23:17:09Z', lastContentAddedAt: 'not a date' }))
            .toBe(Date.parse('2026-08-10T23:17:09Z'));
    });

    // Server timestamps differ in fractional precision, which a string
    // comparison would order wrongly.
    it('orders by instant rather than by text', () => {
        const earlier = '2026-09-14T20:08:21.9Z';
        const later = '2026-09-14T20:08:21.91Z';

        expect(recentlyAddedTime({ addedAt: later })).toBeGreaterThan(recentlyAddedTime({ addedAt: earlier }));
        expect(earlier > later).toBe(true);
    });
});
