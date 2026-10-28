import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import MediaRow from './MediaRow';

const tokensCss = readFileSync('src/styles/tokens.css', 'utf8');

function focusOutlineReachPx(): number {
    const rule = tokensCss.match(/\.vora-media-card:focus-visible > \.vora-media-card-art\s*\{([^}]*)\}/);
    if (!rule) throw new Error('No focus outline rule for .vora-media-card-art');
    const width = rule[1].match(/outline:\s*(\d+)px/);
    const offset = rule[1].match(/outline-offset:\s*(\d+)px/);
    return Number(width?.[1] ?? 0) + Number(offset?.[1] ?? 0);
}

describe('MediaRow', () => {
    it('keeps room inside the scroller for a focused card\'s outline without moving the cards', () => {
        render(
            <MediaRow title="Trending">
                <button type="button">A card</button>
            </MediaRow>,
        );

        const scroller = screen.getByRole('button', { name: 'A card' }).parentElement;
        if (!scroller) throw new Error('Card has no scroller');

        expect(focusOutlineReachPx()).toBeLessThanOrEqual(4);
        expect(scroller.className.split(' ')).toEqual(expect.arrayContaining(['px-1', 'pt-1', '-mx-1', '-mt-1', 'pb-2']));
        expect(scroller.style.scrollPaddingInline).toBe('0.25rem');
    });
});
