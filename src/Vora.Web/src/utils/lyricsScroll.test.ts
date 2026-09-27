import { describe, it, expect } from 'vitest';
import { LyricsAnchor, lyricsScrollTop } from './lyricsScroll';

const viewport = 600;
const lineHeight = 30;

describe('positioning the active lyric line', () => {
    // A song with a long intro: nothing is active yet and the first line is the
    // target. It has to sit at the top, not below a third of a screen of space.
    it('leaves the first line at the top before the lyrics start', () => {
        expect(lyricsScrollTop(24, lineHeight, viewport)).toBe(0);
    });

    it('keeps early lines where they are until they pass the anchor', () => {
        expect(lyricsScrollTop(150, lineHeight, viewport)).toBe(0);
    });

    it('scrolls so a later line sits a third of the way down', () => {
        const lineTop = 1200;
        const scrollTop = lyricsScrollTop(lineTop, lineHeight, viewport);

        expect(lineTop + lineHeight / 2 - scrollTop).toBeCloseTo(viewport * LyricsAnchor, 0);
    });

    it('never scrolls above the top of the list', () => {
        expect(lyricsScrollTop(0, lineHeight, 2000)).toBe(0);
    });

    it('moves further for a line further down', () => {
        expect(lyricsScrollTop(2000, lineHeight, viewport)).toBeGreaterThan(lyricsScrollTop(1000, lineHeight, viewport));
    });

    // A backward seek targets an earlier line, and the position has to follow it
    // back rather than only ever increasing.
    it('scrolls back up for an earlier line', () => {
        expect(lyricsScrollTop(400, lineHeight, viewport)).toBeLessThan(lyricsScrollTop(1600, lineHeight, viewport));
    });

    it('honours a different anchor', () => {
        expect(lyricsScrollTop(1000, 0, viewport, 0.5)).toBe(700);
    });

    it('returns whole pixels', () => {
        expect(Number.isInteger(lyricsScrollTop(1000.4, 17, 333))).toBe(true);
    });
});
