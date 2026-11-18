import { describe, it, expect } from 'vitest';
import { formatLinkCode, isCompleteLinkCode, normalizeLinkCode } from './linkCode';

describe('linkCode', () => {
    it('reads a typed code whatever its case, spaces or dash', () => {
        expect(normalizeLinkCode(' bcdf-ghjk ')).toBe('BCDFGHJK');
        expect(normalizeLinkCode('bcdfghjkmnp')).toBe('BCDFGHJK');
    });

    it('shows the code in two groups of four as it is typed', () => {
        expect(formatLinkCode('bcdfghjk')).toBe('BCDF-GHJK');
        expect(formatLinkCode('bcdfg')).toBe('BCDF-G');
        expect(formatLinkCode('bcd')).toBe('BCD');
    });

    it('is complete at eight characters', () => {
        expect(isCompleteLinkCode('BCDF-GHJK')).toBe(true);
        expect(isCompleteLinkCode('BCDF-GHJ')).toBe(false);
        expect(isCompleteLinkCode('')).toBe(false);
    });
});
