import { describe, it, expect } from 'vitest';
import { dropIndex, moveNavItem } from './navOrder';

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
