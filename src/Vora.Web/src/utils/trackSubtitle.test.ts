import { describe, it, expect } from 'vitest';
import { trackSubtitle } from './trackSubtitle';

describe('track subtitle', () => {
    it('joins artist and album', () => {
        expect(trackSubtitle('blink-182', 'Cheshire Cat')).toBe('blink-182 — Cheshire Cat');
    });

    // The reported case: no artist left a leading separator.
    it.each([undefined, null, '', '   '])('drops a missing artist (%s) without a leading dash', artist => {
        expect(trackSubtitle(artist, 'Cheshire Cat')).toBe('Cheshire Cat');
    });

    it.each([undefined, null, ''])('drops a missing album (%s) without a trailing dash', album => {
        expect(trackSubtitle('blink-182', album)).toBe('blink-182');
    });

    it('is empty when both are missing', () => {
        expect(trackSubtitle(undefined, null)).toBe('');
    });

    it('trims each part', () => {
        expect(trackSubtitle('  blink-182 ', ' Cheshire Cat  ')).toBe('blink-182 — Cheshire Cat');
    });

    it('keeps a separator that is part of a name', () => {
        expect(trackSubtitle('Artist — Band', 'Album')).toBe('Artist — Band — Album');
    });
});
