import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import MediaCard from './MediaCard';

describe('MediaCard', () => {
    const defaults = {
        id: 'mc-1',
        title: 'Test Title',
        onClick: () => { },
    };

    it('renders title and optional subtitle', () => {
        render(<MediaCard {...defaults} subtitle="2024" />);
        expect(screen.getByText('Test Title')).toBeInTheDocument();
        expect(screen.getByText('2024')).toBeInTheDocument();
    });

    it('renders the poster image when imageUrl is provided', () => {
        render(<MediaCard {...defaults} imageUrl="/img/poster.jpg" />);
        const img = screen.getByRole('img', { name: 'Test Title' }) as HTMLImageElement;
        expect(img.src).toContain('/img/poster.jpg');
    });

    it('renders "No Image" placeholder when no imageUrl', () => {
        render(<MediaCard {...defaults} />);
        expect(screen.getByText('No Image')).toBeInTheDocument();
    });

    it('renders a 4-image grid when multiPosters has at least 4 entries', () => {
        const { container } = render(<MediaCard {...defaults} multiPosters={['a.jpg', 'b.jpg', 'c.jpg', 'd.jpg']} />);
        // The 4-grid images use alt="" (decorative, role="presentation"), so query by tag.
        const imgs = container.querySelectorAll('img');
        expect(imgs).toHaveLength(4);
        expect(imgs[0].src).toContain('a.jpg');
        expect(imgs[3].src).toContain('d.jpg');
    });

    it('falls back to single image when multiPosters has fewer than 4 entries', () => {
        const { container } = render(<MediaCard {...defaults} multiPosters={['a.jpg', 'b.jpg']} imageUrl="/img/p.jpg" />);
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
        // Played checkmark is the only checkmark-shaped path with d starting "M5 13l4 4L19 7"
        const paths = container.querySelectorAll('svg path');
        const hasCheck = Array.from(paths).some(p => p.getAttribute('d') === 'M5 13l4 4L19 7');
        expect(hasCheck).toBe(true);
    });

    it('renders a progress bar with width matching progressPercent', () => {
        const { container } = render(<MediaCard {...defaults} progressPercent={42} />);
        const bar = container.querySelector('div[style*="width: 42%"]') as HTMLDivElement | null;
        expect(bar).not.toBeNull();
    });

    it('does not render a progress bar when progressPercent is 0', () => {
        const { container } = render(<MediaCard {...defaults} progressPercent={0} />);
        const bar = container.querySelector('div[style*="width: 0%"]');
        expect(bar).toBeNull();
    });

    it('fires onDelete when admin delete button is clicked', () => {
        const onDelete = vi.fn();
        render(<MediaCard {...defaults} isAdmin onDelete={onDelete} />);
        const deleteBtn = screen.getByTitle('Delete');
        fireEvent.click(deleteBtn);
        expect(onDelete).toHaveBeenCalledOnce();
    });

    it('does not render delete button when isAdmin is false', () => {
        const onDelete = vi.fn();
        render(<MediaCard {...defaults} onDelete={onDelete} />);
        expect(screen.queryByTitle('Delete')).toBeNull();
    });

    it('fires onHide when hide button is clicked', () => {
        const onHide = vi.fn();
        render(<MediaCard {...defaults} onHide={onHide} />);
        const hideBtn = screen.getByTitle('Remove');
        fireEvent.click(hideBtn);
        expect(onHide).toHaveBeenCalledOnce();
    });

    it('shows playlist icon (svg) instead of "No Image" for Playlist type', () => {
        render(<MediaCard {...defaults} type="Playlist" />);
        expect(screen.queryByText('No Image')).toBeNull();
    });

    it.each([
        ['poster', 'aspect-[2/3]'],
        ['video', 'aspect-video'],
        ['square', 'aspect-square'],
    ] as const)('applies aspect ratio class %s -> %s', (aspect, expected) => {
        const { container } = render(<MediaCard {...defaults} aspectRatio={aspect} />);
        const card = container.querySelector(`.${CSS.escape(expected)}`);
        expect(card).not.toBeNull();
    });
});
