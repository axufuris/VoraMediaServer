import { describe, it, expect } from 'vitest';
import { cueAreaOf, cuesToMove, placeCues, shouldReposition, SubtitleCueLine, SubtitleLineShare, textCues, WholeVideo, type PlaceableCue } from './subtitleCuePlacement';

describe('cue repositioning', () => {
    it('lifts a cue that has no position of its own', () => {
        expect(shouldReposition({ line: 'auto' })).toBe(true);
    });

    it.each([0, 1, -1, 5])('leaves a cue that was positioned at line %s alone', line => {
        expect(shouldReposition({ line })).toBe(false);
    });

    it('places lifted cues above the bottom edge', () => {
        expect(SubtitleCueLine).toBeLessThan(0);
    });

    it('applies the line and snapping to every auto cue', () => {
        const cues: PlaceableCue[] = [
            { line: 'auto', snapToLines: false, size: 100 },
            { line: 'auto', snapToLines: false, size: 100 },
        ];

        placeCues(cuesToMove(cues));

        expect(cues.map(c => c.line)).toEqual([SubtitleCueLine, SubtitleCueLine]);
        expect(cues.every(c => c.snapToLines)).toBe(true);
        expect(cues.map(c => c.size)).toEqual([100, 100]);
    });

    it('leaves positioned cues untouched while moving the rest', () => {
        const cues: PlaceableCue[] = [
            { line: 'auto', snapToLines: false, size: 100 },
            { line: 0, snapToLines: true, size: 100 },
        ];

        placeCues(cuesToMove(cues));

        expect(cues[0].line).toBe(SubtitleCueLine);
        expect(cues[1].line).toBe(0);
    });

    it('does nothing when there are no cues yet', () => {
        expect(textCues(null)).toEqual([]);
        expect(cuesToMove([])).toEqual([]);
    });

    it('moves the same cues again when the picture moves, and never wider than the file made them', () => {
        const cues: PlaceableCue[] = [
            { line: 'auto', snapToLines: false, size: 100 },
            { line: 'auto', snapToLines: false, size: 60 },
        ];
        const moved = cuesToMove(cues);

        placeCues(moved, { lift: 2, size: 80 });
        expect(cues.map(c => [c.line, c.size])).toEqual([[SubtitleCueLine - 2, 80], [SubtitleCueLine - 2, 60]]);

        placeCues(moved, WholeVideo);
        expect(cues.map(c => [c.line, c.size])).toEqual([[SubtitleCueLine, 100], [SubtitleCueLine, 60]]);
    });
});

describe('where cues may sit once the picture is enlarged past the window', () => {
    it('is the whole video when the picture is laid out by the browser', () => {
        expect(cueAreaOf(null, { width: 1600, height: 900 })).toBe(WholeVideo);
    });

    it('lifts cues by the lines hidden under the bottom edge', () => {
        const layout = { width: 1800, height: 1012.5, left: -100, top: -56.25 };

        const area = cueAreaOf(layout, { width: 1600, height: 900 });

        expect(area.lift).toBeCloseTo(56.25 / (SubtitleLineShare * 1012.5));
        expect(area.size).toBeCloseTo(100 * 1600 / 1800);
    });

    it('keeps them centred inside what shows when the picture is off centre', () => {
        const layout = { width: 2000, height: 900, left: -300, top: 0 };

        const area = cueAreaOf(layout, { width: 1600, height: 900 });

        expect(area.lift).toBe(0);
        expect(area.size).toBeCloseTo(100 * 1400 / 2000);
    });

    it('only lifts cues off the hidden bar when the picture fits inside the window', () => {
        const layout = { width: 1350, height: 759.375, left: 125, top: -42.1875 };

        const area = cueAreaOf(layout, { width: 1600, height: 675 });

        expect(area.lift).toBeCloseTo(42.1875 / (SubtitleLineShare * 759.375));
        expect(area.size).toBe(100);
    });
});
