import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { CheckGlyph, CornerChip, PosterCorner, UnplayedCountBadge, WatchedBadge } from './WatchedBadge';
import { cornerLength } from './posterCornerLength';
import { episodeCornerLabel } from '../../../utils/posterCaption';

// The check is the same path the Android client rasterises. If these two ever
// diverge, the same item looks different on phone and browser — which is the
// drift this component exists to stop.
const CHECK_PATH = 'M5 13l4 4L19 7';

// The value a style holds once the browser has read it, which simplifies calc().
function css(property: string, value: string): string {
    const probe = document.createElement('div');
    probe.style.setProperty(property, value);
    const read = probe.style.getPropertyValue(property);
    if (!read) throw new Error(`${property}: ${value} is not a length the browser keeps`);
    return read;
}

describe('watched badge', () => {
    it('draws the same check path the Android client uses', () => {
        const { container } = render(<CheckGlyph size="12px" />);

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
        const { container } = render(<CornerChip label="E1" />);

        expect(container.textContent).toContain('E1');
        expect(container.querySelector('path')).toBeNull();
    });

    it('a watched episode shows the check to the left of the same number', () => {
        const { container } = render(<CornerChip label="E1" watched />);

        expect(container.textContent).toContain('E1');
        expect(container.querySelector('path')?.getAttribute('d')).toBe(CHECK_PATH);

        // Leading, not trailing — the glyph precedes the label text.
        const chip = container.firstElementChild!;
        expect(chip.firstElementChild?.tagName.toLowerCase()).toBe('svg');
    });

    it('colours the check with the accent token rather than a fixed colour', () => {
        const { container } = render(<WatchedBadge />);

        expect((container.querySelector('svg') as SVGElement).style.color).toContain('--vora-accent-500');
    });

    // One box on every poster: a watched movie and a watched episode share the
    // chip's shape, so the check never switches between a circle and a box.
    it('draws a watched movie in the same box as an episode chip', () => {
        const movie = render(<WatchedBadge />).container.firstElementChild as HTMLElement;
        const episode = render(<CornerChip label="E1" watched />).container.firstElementChild as HTMLElement;

        expect(movie.style.borderRadius).toBe(episode.style.borderRadius);
        expect(movie.style.background).toBe(episode.style.background);
        expect(movie.className).not.toContain('rounded-full');
    });

    // The wrapper is a flex box so the chip hugs the corner; as a plain block
    // the chip sat on a text baseline several pixels below it.
    it('pins the badge 6 in from the corner of a 128-wide tile', () => {
        const tile = render(<PosterCorner><WatchedBadge /></PosterCorner>).container.firstElementChild as HTMLElement;
        const corner = tile.firstElementChild as HTMLElement;

        expect(corner.className).toContain('flex');
        expect(corner.style.top).toBe(css('top', cornerLength(6)));
        expect(corner.style.right).toBe(css('right', cornerLength(6)));
    });

    // The overlay icons are part of the artwork and shrink with it, so a box
    // held at a fixed size grew over them on every tile smaller than the
    // editor's: the TV's posters drew it nearly twice as large.
    it('measures every length from the tile it sits on, at any size', () => {
        const tile = render(<PosterCorner><CornerChip label="E1" watched /></PosterCorner>).container.firstElementChild as HTMLElement;
        const chip = tile.querySelector('span') as HTMLElement;
        const check = tile.querySelector('svg') as SVGElement;

        expect(tile.style.getPropertyValue('container-type')).toBe('inline-size');
        expect(tile.className).toContain('inset-0');
        expect(chip.style.fontSize).toBe(css('font-size', cornerLength(10)));
        expect(chip.style.padding).toBe(css('padding', `${cornerLength(2)} ${cornerLength(6)}`));
        expect(chip.style.gap).toBe(css('gap', cornerLength(4)));
        expect(chip.style.borderRadius).toBe(css('border-radius', cornerLength(4)));
        expect(check.style.width).toBe(css('width', cornerLength(10)));
    });

    // A 16:9 still is barely half as tall as it is wide, so the box measured on
    // its whole width stood taller than the badges baked into the artwork.
    it('measures a landscape tile on the right-hand three quarters of it', () => {
        const poster = render(<PosterCorner><WatchedBadge /></PosterCorner>).container.firstElementChild as HTMLElement;
        const still = render(<PosterCorner landscape><WatchedBadge /></PosterCorner>).container.firstElementChild as HTMLElement;

        expect(poster.style.left).toBe('');
        expect(still.style.left).toBe('25%');
        expect(still.className).toContain('inset-0');
        expect(still.style.getPropertyValue('container-type')).toBe('inline-size');
    });

    it('sizes the check alone and the episode count the same way', () => {
        const check = render(<WatchedBadge />).container.firstElementChild as HTMLElement;
        const count = render(<UnplayedCountBadge count={3} />).container.firstElementChild as HTMLElement;

        expect(check.style.padding).toBe(css('padding', `${cornerLength(2)} ${cornerLength(4)}`));
        expect((check.querySelector('svg') as SVGElement).style.width).toBe(css('width', cornerLength(12)));
        expect(count.style.fontSize).toBe(css('font-size', cornerLength(10)));
        expect(count.style.minWidth).toBe(css('min-width', cornerLength(18)));
        expect(count.style.padding).toBe(css('padding', `${cornerLength(2)} ${cornerLength(5)}`));
    });

    it('states its lengths as shares of a 128-wide tile', () => {
        expect(cornerLength(6)).toBe('calc(100cqw * 6 / 128)');
        expect(cornerLength(128)).toBe('calc(100cqw * 128 / 128)');
    });
});

// One function decides the label, so a card in a rail and a row in the season
// list cannot describe the same episode differently.
describe('episode corner label', () => {
    it('labels a single episode', () => {
        expect(episodeCornerLabel({ type: 'Episode', episodeNumber: 1 })).toBe('E1');
    });

    it('spans a file that holds two episodes', () => {
        expect(episodeCornerLabel({ type: 'Episode', episodeNumber: 1, endEpisodeNumber: 2 })).toBe('E1-E2');
    });

    // An end number equal to (or below) the start is not a range.
    it('ignores an end number that does not extend the range', () => {
        expect(episodeCornerLabel({ type: 'Episode', episodeNumber: 3, endEpisodeNumber: 3 })).toBe('E3');
    });

    it.each(['Movie', 'TvShow', 'Season', 'Track', undefined])('gives nothing for %s', type => {
        expect(episodeCornerLabel({ type, episodeNumber: 4 })).toBeNull();
    });

    it('gives nothing for an episode with no number', () => {
        expect(episodeCornerLabel({ type: 'Episode', episodeNumber: null })).toBeNull();
    });

    it('gives nothing when there is no item at all', () => {
        expect(episodeCornerLabel(undefined)).toBeNull();
    });
});
