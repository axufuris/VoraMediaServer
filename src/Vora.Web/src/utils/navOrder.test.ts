import { describe, it, expect } from 'vitest';
import { dropIndex, moveNavItem, moveShownNavItem } from './navOrder';

const items = () => [
    { id: 'music', order: 0 },
    { id: 'movies', order: 1 },
    { id: 'shows', order: 2 },
    { id: 'radio', order: 3 },
];

describe('moveNavItem', () => {
    it('moves an item and renumbers the order', () => {
        const moved = moveNavItem(items(), 0, 2);

        expect(moved.map(i => i.id)).toEqual(['movies', 'shows', 'music', 'radio']);
        expect(moved.map(i => i.order)).toEqual([0, 1, 2, 3]);
    });

    it('returns new objects and leaves the originals alone', () => {
        const original = items();
        moveNavItem(original, 3, 0);

        expect(original.map(i => i.order)).toEqual([0, 1, 2, 3]);
    });

    it('clamps a target past either end', () => {
        expect(moveNavItem(items(), 1, 99).map(i => i.id)).toEqual(['music', 'shows', 'radio', 'movies']);
        expect(moveNavItem(items(), 2, -5).map(i => i.id)).toEqual(['shows', 'music', 'movies', 'radio']);
    });
});

describe('moveShownNavItem', () => {
    const sidebar = () => [
        { id: 'music', order: 0 },
        { id: 'podcasts', order: 1 },
        { id: 'livetv', order: 2 },
        { id: 'radio', order: 3 },
        { id: 'discovery', order: 4 },
        { id: 'calendar', order: 5 },
        { id: 'movies', order: 6 },
        { id: 'shows', order: 7 },
    ];
    const radioOff = (item: { id: string }) => item.id !== 'radio';
    const shownIds = (list: { id: string; order: number }[]) => [...list].sort((a, b) => a.order - b.order).filter(radioOff).map(i => i.id);

    // Issue #144: with Radio off, the editor lists music, podcasts, livetv,
    // discovery, calendar, movies, shows. Dragging Movies (row 5) above Discover
    // (row 3) used to read row 5 of the full list, which is Calendar, so
    // Calendar moved instead and Movies stayed where it was.
    it('lands where it was dropped in the list the editor shows when a section is turned off', () => {
        const moved = moveShownNavItem(sidebar(), radioOff, 5, 3);

        expect(shownIds(moved)).toEqual(['music', 'podcasts', 'livetv', 'movies', 'discovery', 'calendar', 'shows']);
    });

    it('drops at the bottom of the shown list', () => {
        const moved = moveShownNavItem(sidebar(), radioOff, 0, 6);

        expect(shownIds(moved)).toEqual(['podcasts', 'livetv', 'discovery', 'calendar', 'movies', 'shows', 'music']);
    });

    it('keeps the hidden section in its slot for when it is turned back on', () => {
        const moved = moveShownNavItem(sidebar(), radioOff, 5, 3);

        expect(moved.map(i => i.id)).toEqual(['music', 'podcasts', 'livetv', 'radio', 'movies', 'discovery', 'calendar', 'shows']);
        expect(moved.map(i => i.order)).toEqual([0, 1, 2, 3, 4, 5, 6, 7]);
    });

    it('moves exactly like moveNavItem when nothing is hidden', () => {
        expect(moveShownNavItem(sidebar(), () => true, 6, 0)).toEqual(moveNavItem(sidebar(), 6, 0));
    });
});

describe('dropIndex', () => {
    it('lands above or below the row under the pointer', () => {
        expect(dropIndex(0, 2, false)).toBe(1);
        expect(dropIndex(0, 2, true)).toBe(2);
        expect(dropIndex(3, 0, false)).toBe(0);
        expect(dropIndex(3, 0, true)).toBe(1);
    });

    it('is a no-op when dropped just around itself', () => {
        expect(dropIndex(1, 1, false)).toBe(1);
        expect(dropIndex(1, 1, true)).toBe(1);
        expect(dropIndex(1, 0, true)).toBe(1);
    });
});
