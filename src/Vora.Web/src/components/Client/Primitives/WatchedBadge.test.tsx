import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { CheckGlyph, CornerLabelChip, WatchedBadge } from './WatchedBadge';

// The check is the same path the Android client rasterises. If these two ever
// diverge, the same item looks different on phone and browser — which is the
// drift this component exists to stop.
const CHECK_PATH = 'M5 13l4 4L19 7';

describe('watched badge', () => {
    it('draws the same check path the Android client uses', () => {
        const { container } = render(<CheckGlyph size={12} />);

        expect(container.querySelector('path')?.getAttribute('d')).toBe(CHECK_PATH);
    });

    it('the standalone badge is a check and nothing else', () => {
        const { container } = render(<WatchedBadge />);

        expect(container.querySelector('path')?.getAttribute('d')).toBe(CHECK_PATH);
        expect(container.textContent).toBe('');
    });

    // An episode chip keeps its number whether or not it has been watched, so
    // marking one played does not shift the chip's position on the card.
    it('an unwatched episode shows its number with no check', () => {
        const { container } = render(<CornerLabelChip label="E1" />);

        expect(container.textContent).toContain('E1');
        expect(container.querySelector('path')).toBeNull();
    });

    it('a watched episode shows the check to the left of the same number', () => {
        const { container } = render(<CornerLabelChip label="E1" watched />);

        expect(container.textContent).toContain('E1');
        expect(container.querySelector('path')?.getAttribute('d')).toBe(CHECK_PATH);

        // Leading, not trailing — the glyph precedes the label text.
        const chip = container.firstElementChild!;
        expect(chip.firstElementChild?.tagName.toLowerCase()).toBe('svg');
    });

    it('colours the check with the accent token rather than a fixed colour', () => {
        const { container } = render(<WatchedBadge />);

        expect((container.firstElementChild as HTMLElement).style.color).toContain('--vora-accent-500');
    });
});
