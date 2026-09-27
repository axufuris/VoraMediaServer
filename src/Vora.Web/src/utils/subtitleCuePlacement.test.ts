import { describe, it, expect } from 'vitest';
import { shouldReposition, placeCues, SubtitleCueLine } from './subtitleCuePlacement';

// The browser positions cues against the <video> element's box, not the picture
// inside it. The player fills the viewport with object-contain, so the default
// bottom placement lands the text in the black bar under the picture and on top
// of the transport controls — which is exactly what a hard-matted scope film in
// a 16:9 frame produces.
describe('cue repositioning', () => {
    it('lifts a cue that has no position of its own', () => {
        expect(shouldReposition({ line: 'auto' })).toBe(true);
    });

    // A cue placed deliberately — at the top to dodge burned-in signage, say —
    // is positioned for a reason, and moving it would undo that.
    it.each([0, 1, -1, 5])('leaves a cue that was positioned at line %s alone', line => {
        expect(shouldReposition({ line })).toBe(false);
    });

    it('places lifted cues above the bottom edge', () => {
        expect(SubtitleCueLine).toBeLessThan(0);
    });

    it('applies the line and snapping to every auto cue', () => {
        const cues = [
            { line: 'auto' as number | 'auto', snapToLines: false },
            { line: 'auto' as number | 'auto', snapToLines: false },
        ];

        placeCues(cues as unknown as TextTrackCueList);

        expect(cues.map(c => c.line)).toEqual([SubtitleCueLine, SubtitleCueLine]);
        expect(cues.every(c => c.snapToLines)).toBe(true);
    });

    it('leaves positioned cues untouched while moving the rest', () => {
        const cues = [
            { line: 'auto' as number | 'auto', snapToLines: false },
            { line: 0 as number | 'auto', snapToLines: true },
        ];

        placeCues(cues as unknown as TextTrackCueList);

        expect(cues[0].line).toBe(SubtitleCueLine);
        expect(cues[1].line).toBe(0);
    });

    // Cues do not exist until the file has loaded, and the track fires events
    // before then.
    it('does nothing when there are no cues yet', () => {
        expect(() => placeCues(null)).not.toThrow();
    });
});
