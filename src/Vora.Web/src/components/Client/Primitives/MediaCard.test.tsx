import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import MediaCard from './MediaCard';

describe('MediaCard', () => {
    const defaults = { title: 'Test Title' };

    it('renders title and caption lines', () => {
        render(<MediaCard {...defaults} captionLines={['2024', 'Directors Cut']} />);
        expect(screen.getByText('Test Title')).toBeInTheDocument();
        expect(screen.getByText('2024')).toBeInTheDocument();
        expect(screen.getByText('Directors Cut')).toBeInTheDocument();
    });

    it('captions itself from the item type when given an item', () => {
        render(<MediaCard item={{ type: 'Episode', title: 'Pilot', tvShowTitle: 'The Show', seasonNumber: 1, episodeNumber: 2 }} />);
        expect(screen.getByText('The Show')).toBeInTheDocument();
        expect(screen.getByText('S1 · E2')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
    });

    it('captions a collection with its item count', () => {
        render(<MediaCard item={{ type: 'Collection', title: 'Star Wars', itemCount: 11 }} />);
        expect(screen.getByText('11 items')).toBeInTheDocument();
    });

    it('renders the artwork when imageUrl is provided', () => {
        render(<MediaCard {...defaults} imageUrl="/img/poster.jpg" />);
        const img = screen.getByRole('img', { name: 'Test Title' }) as HTMLImageElement;
        expect(img.src).toContain('/img/poster.jpg');
    });

    it('renders the branded placeholder (no img element) when no imageUrl', () => {
        const { container } = render(<MediaCard {...defaults} />);
        expect(container.querySelector('img')).toBeNull();
    });

    it('renders a 4-image mosaic when mosaicUrls has at least 4 entries', () => {
        const { container } = render(<MediaCard {...defaults} mosaicUrls={['a.jpg', 'b.jpg', 'c.jpg', 'd.jpg']} />);
        const imgs = container.querySelectorAll('img');
        expect(imgs).toHaveLength(4);
        expect(imgs[0].src).toContain('a.jpg');
        expect(imgs[3].src).toContain('d.jpg');
    });

    it('renders a 2-image split when mosaicUrls has exactly 2 entries', () => {
        const { container } = render(<MediaCard {...defaults} mosaicUrls={['a.jpg', 'b.jpg']} imageUrl="/img/p.jpg" />);
        const imgs = container.querySelectorAll('img');
        expect(imgs).toHaveLength(2);
        expect(imgs[0].src).toContain('a.jpg');
    });

    it('falls back to the single image when mosaicUrls has fewer than 2 entries', () => {
        const { container } = render(<MediaCard {...defaults} mosaicUrls={['a.jpg']} imageUrl="/img/p.jpg" />);
        const imgs = container.querySelectorAll('img');
        expect(imgs).toHaveLength(1);
        expect(imgs[0].src).toContain('p.jpg');
    });

    it('fires onClick when the card is clicked', () => {
        const onClick = vi.fn();
        render(<MediaCard {...defaults} onClick={onClick} />);
        fireEvent.click(screen.getByText('Test Title'));
        expect(onClick).toHaveBeenCalledOnce();
    });

    it('shows the unplayed-count badge when count > 0', () => {
        render(<MediaCard {...defaults} unplayedCount={3} />);
        expect(screen.getByText('3')).toBeInTheDocument();
    });

    it('hides the unplayed-count badge when count is 0', () => {
        render(<MediaCard {...defaults} unplayedCount={0} />);
        expect(screen.queryByText('0')).toBeNull();
    });

    it('renders the played checkmark when isPlayed', () => {
        const { container } = render(<MediaCard {...defaults} isPlayed />);
        const paths = container.querySelectorAll('svg path');
        const hasCheck = Array.from(paths).some(p => p.getAttribute('d') === 'M5 13l4 4L19 7');
        expect(hasCheck).toBe(true);
    });

    it('renders a progress bar with width matching progressPercent', () => {
        const { container } = render(<MediaCard {...defaults} progressPercent={42} />);
        expect(container.querySelector('div[style*="width: 42%"]')).not.toBeNull();
    });

    it('does not render a progress bar when progressPercent is 0', () => {
        const { container } = render(<MediaCard {...defaults} progressPercent={0} />);
        expect(container.querySelector('div[style*="width: 0%"]')).toBeNull();
    });

    it('fires onDelete when the delete button is clicked', () => {
        const onDelete = vi.fn();
        render(<MediaCard {...defaults} onDelete={onDelete} />);
        fireEvent.click(screen.getByTitle('Delete'));
        expect(onDelete).toHaveBeenCalledOnce();
    });

    it('does not render a delete button without onDelete', () => {
        render(<MediaCard {...defaults} />);
        expect(screen.queryByTitle('Delete')).toBeNull();
    });

    it('fires onRemove when the remove button is clicked', () => {
        const onRemove = vi.fn();
        render(<MediaCard {...defaults} onRemove={onRemove} />);
        fireEvent.click(screen.getByTitle('Remove'));
        expect(onRemove).toHaveBeenCalledOnce();
    });

    it.each([
        ['poster', '2 / 3'],
        ['still', '16 / 9'],
        ['square', '1 / 1'],
    ] as const)('applies the %s aspect ratio', (shape, expected) => {
        const { container } = render(<MediaCard {...defaults} shape={shape} />);
        const art = container.querySelector(`div[style*="aspect-ratio: ${expected}"]`);
        expect(art).not.toBeNull();
    });

    it('sizes from the card width token unless filling its grid cell', () => {
        const { container: sized } = render(<MediaCard {...defaults} size="lg" />);
        expect((sized.firstElementChild as HTMLElement).style.width).toContain('--vora-card-w-lg');

        const { container: dense } = render(<MediaCard {...defaults} size="xs" />);
        expect((dense.firstElementChild as HTMLElement).style.width).toContain('--vora-card-w-xs');

        const { container: filled } = render(<MediaCard {...defaults} fill />);
        expect((filled.firstElementChild as HTMLElement).style.width).toBe('100%');
    });

    // An episode looks the same wherever it is drawn — a rail card here, a row
    // in the season list — so the number chip has to come from the card itself
    // rather than from whatever the call site remembered to pass.
    describe('episode corner chip', () => {
        const episode = { type: 'Episode', title: 'Pilot', tvShowTitle: 'The Show', seasonNumber: 1, episodeNumber: 2 };

        it('labels an episode card with its number without being asked to', () => {
            render(<MediaCard item={episode} />);

            expect(screen.getByText('E2')).toBeInTheDocument();
        });

        it('folds the watched check into that chip instead of a second badge', () => {
            const { container } = render(<MediaCard item={episode} isPlayed />);

            // Scoped to the check's own path — the card also draws a
            // placeholder graphic when it has no artwork.
            const checks = container.querySelectorAll('path[d="M5 13l4 4L19 7"]');
            expect(checks).toHaveLength(1);
            expect(screen.getByText('E2').closest('span')!.contains(checks[0])).toBe(true);
        });

        it('gives a movie the standalone badge and no number', () => {
            render(<MediaCard item={{ type: 'Movie', title: 'Dune' }} isPlayed />);

            expect(screen.queryByText(/^E\d/)).toBeNull();
        });

        it('leaves an explicit badge in charge of the corner', () => {
            render(<MediaCard item={episode} isPlayed badge={<span>LIVE</span>} />);

            expect(screen.getByText('LIVE')).toBeInTheDocument();
            expect(screen.queryByText('E2')).toBeNull();
        });
    });
});
