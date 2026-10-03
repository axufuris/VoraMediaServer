import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import ArtistBiography from './ArtistBiography';

describe('ArtistBiography', () => {
    it('credits Last.fm and expands a long biography on request', () => {
        render(<ArtistBiography text={'Daft Punk were a French duo. '.repeat(30)} />);

        expect(screen.getByRole('heading', { name: 'About' })).toBeInTheDocument();
        expect(screen.getByText('From Last.fm')).toBeInTheDocument();
        const toggle = screen.getByRole('button', { name: 'Read more' });
        expect(toggle).toHaveAttribute('aria-expanded', 'false');

        fireEvent.click(toggle);

        expect(screen.getByRole('button', { name: 'Show less' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('offers no toggle for a short biography', () => {
        render(<ArtistBiography text="A short one." />);

        expect(screen.queryByRole('button')).toBeNull();
    });
});
