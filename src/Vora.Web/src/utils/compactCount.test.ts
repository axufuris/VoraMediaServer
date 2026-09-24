import { describe, expect, it } from 'vitest';
import { formatCompactCount } from './compactCount';

describe('formatCompactCount', () => {
    it('shortens large counts', () => {
        expect(formatCompactCount(2_140_000)).toMatch(/^2\.1\s?M$/);
        expect(formatCompactCount(348_000)).toMatch(/^348\s?K$/);
    });

    it('leaves small counts alone', () => {
        expect(formatCompactCount(912)).toBe('912');
    });

    it('has nothing to say about a missing or nonsensical count', () => {
        expect(formatCompactCount(null)).toBeNull();
        expect(formatCompactCount(undefined)).toBeNull();
        expect(formatCompactCount(-1)).toBeNull();
        expect(formatCompactCount(Number.NaN)).toBeNull();
    });
});
