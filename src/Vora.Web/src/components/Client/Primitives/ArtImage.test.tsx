import { describe, it, expect } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ArtImage from './ArtImage';

// A failed image used to be cleared by an effect on the next render. Tracking
// which source failed makes a new source start un-failed immediately.
describe('ArtImage', () => {
    it('shows the image while it loads', () => {
        render(<ArtImage src="https://example.test/a.jpg" alt="Poster A" />);

        expect(screen.getByRole('img', { name: 'Poster A' })).toHaveAttribute('src', 'https://example.test/a.jpg');
    });

    it('falls back to the placeholder when the image fails', () => {
        const { container } = render(<ArtImage src="https://example.test/a.jpg" alt="Poster A" />);

        fireEvent.error(screen.getByRole('img', { name: 'Poster A' }));

        expect(container.querySelector('img')).toBeNull();
        expect(screen.getByRole('img', { name: 'Poster A' })).toBeInTheDocument();
    });

    it('tries a new source after the previous one failed', () => {
        const { container, rerender } = render(<ArtImage src="https://example.test/a.jpg" alt="Poster" />);
        fireEvent.error(screen.getByRole('img', { name: 'Poster' }));

        rerender(<ArtImage src="https://example.test/b.jpg" alt="Poster" />);

        expect(container.querySelector('img')).toHaveAttribute('src', 'https://example.test/b.jpg');
    });

    it('keeps the placeholder for the source that already failed', () => {
        const { container, rerender } = render(<ArtImage src="https://example.test/a.jpg" alt="Poster" />);
        fireEvent.error(screen.getByRole('img', { name: 'Poster' }));

        rerender(<ArtImage src="https://example.test/a.jpg" alt="Poster" />);

        expect(container.querySelector('img')).toBeNull();
    });

    it('shows the placeholder when there is no source', () => {
        const { container } = render(<ArtImage src={null} alt="Poster" />);

        expect(container.querySelector('img')).toBeNull();
        expect(screen.getByRole('img', { name: 'Poster' })).toBeInTheDocument();
    });
});
