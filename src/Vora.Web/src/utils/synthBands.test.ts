import { describe, expect, it } from 'vitest';
import { SYNTH_BANDS, bandEdges, bandLevels, fallPeaks, idleLevels, settle } from './synthBands';

describe('synth bands', () => {
    it('spreads the bands from the bass to the treble without leaving the analyser', () => {
        const edges = bandEdges(1024, 48000);

        expect(edges).toHaveLength(SYNTH_BANDS + 1);
        expect(edges[0]).toBeGreaterThanOrEqual(0);
        expect(edges[edges.length - 1]).toBeLessThanOrEqual(1023);
        edges.slice(1).forEach((edge, i) => expect(edge).toBeGreaterThanOrEqual(edges[i]));
    });

    it('gives each band the loudest bin in it, from 0 to 1', () => {
        expect(bandLevels([0, 51, 255, 102, 0], [0, 2, 4])).toEqual([0.2, 1]);
    });

    it('lets a band jump up at once but fall back gradually', () => {
        const risen = settle([0.1], [0.9]);
        const falling = settle(risen, [0]);

        expect(risen[0]).toBe(0.9);
        expect(falling[0]).toBeLessThan(0.9);
        expect(falling[0]).toBeGreaterThan(0.8);
    });

    it('holds each peak above its band and lowers it slowly', () => {
        const peaks = fallPeaks([0.8], [0.2]);

        expect(peaks[0]).toBeLessThan(0.8);
        expect(peaks[0]).toBeGreaterThan(0.75);
        expect(fallPeaks([0.1], [0.5])).toEqual([0.5]);
    });

    it('breathes gently when there is no sound to draw', () => {
        const levels = idleLevels(SYNTH_BANDS, 12.3);

        expect(levels).toHaveLength(SYNTH_BANDS);
        levels.forEach(level => {
            expect(level).toBeGreaterThan(0.04);
            expect(level).toBeLessThan(0.1);
        });
    });
});
