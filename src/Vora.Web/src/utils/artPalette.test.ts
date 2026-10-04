import { describe, it, expect } from 'vitest';
import { lighten, paletteFromPixels, withAlpha } from './artPalette';

const pixels = (...colors: [number, number, number, number][]): number[] =>
    colors.flatMap(([r, g, b, count]) => Array.from({ length: count }, () => [r, g, b, 255]).flat());

const hueOf = (hex: string): number => {
    const n = parseInt(hex.slice(1), 16);
    const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(v => v / 255);
    const max = Math.max(r, g, b);
    const d = max - Math.min(r, g, b);
    const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return (h * 60 + 360) % 360;
};

describe('paletteFromPixels', () => {
    it('takes its main colour from the cover', () => {
        const palette = paletteFromPixels(pixels([20, 160, 70, 700], [10, 10, 10, 300]));

        expect(palette).not.toBeNull();
        expect(hueOf(palette?.[0] ?? '#000000')).toBeGreaterThan(110);
        expect(hueOf(palette?.[0] ?? '#000000')).toBeLessThan(160);
    });

    it('uses a second colour from the cover when it has one', () => {
        const palette = paletteFromPixels(pixels([230, 40, 140, 500], [240, 150, 40, 250]));

        expect(Math.abs(hueOf(palette?.[0] ?? '#000000') - hueOf(palette?.[1] ?? '#000000'))).toBeGreaterThan(40);
    });

    it('leaves a grey cover to the theme', () => {
        expect(paletteFromPixels(pixels([128, 128, 128, 600], [20, 20, 20, 400]))).toBeNull();
        expect(paletteFromPixels([])).toBeNull();
    });

    it('makes a dark cover bright enough to glow', () => {
        const palette = paletteFromPixels(pixels([0, 60, 40, 800]));

        expect(palette?.[2]).toMatch(/^#[0-9a-f]{6}$/);
        expect(palette?.[0]).not.toBe('#003c28');
    });
});

describe('colour helpers', () => {
    it('adds transparency to hex and rgb colours', () => {
        expect(withAlpha('#f59e0b', 0.5)).toBe('rgba(245, 158, 11, 0.5)');
        expect(withAlpha('rgb(1, 2, 3)', 0.2)).toBe('rgba(1, 2, 3, 0.2)');
        expect(withAlpha('currentColor', 0.2)).toBe('currentColor');
    });

    it('lightens a colour', () => {
        expect(lighten('#f59e0b', 0.2)).not.toBe('#f59e0b');
        expect(lighten('not-a-colour', 0.2)).toBe('not-a-colour');
    });
});
