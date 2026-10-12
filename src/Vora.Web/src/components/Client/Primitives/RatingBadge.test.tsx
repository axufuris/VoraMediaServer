import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import RatingBadge from './RatingBadge';
import { formatRatingValue, isPercentScaleRating } from '../../../utils/ratings';

function hasFill(root: HTMLElement, selector: string, fill: string): boolean {
    return Array.from(root.querySelectorAll<SVGElement>(selector)).some(el => el.style.fill === fill);
}

describe('isPercentScaleRating', () => {
    it.each([
        'Rotten Tomatoes',
        'rotten tomatoes critic',
        'RT Audience',
        'Metacritic',
    ])('treats %s as a percentage', name => {
        expect(isPercentScaleRating(name)).toBe(true);
    });

    it.each([
        'IMDb',
        'Internet Movie Database',
        'The Movie Database',
        'Trakt',
        undefined,
    ])('treats %s as a 0-10 score', name => {
        expect(isPercentScaleRating(name)).toBe(false);
    });
});

describe('formatRatingValue', () => {
    it('rounds percentage scales to a whole number', () => {
        expect(formatRatingValue(93.6, 'Rotten Tomatoes')).toBe('94%');
    });

    it('keeps one decimal on 0-10 scales', () => {
        expect(formatRatingValue(8, 'IMDb')).toBe('8.0');
    });
});

describe('RatingBadge', () => {
    it('renders the score', () => {
        render(<RatingBadge value={8.5} name="IMDb" />);
        expect(screen.getByText('8.5')).toBeInTheDocument();
    });

    it('does not spell out the provider name for a known platform', () => {
        render(<RatingBadge value={5.9} name="Internet Movie Database" />);
        expect(screen.queryByText('Internet Movie Database')).toBeNull();
    });

    it('keeps the provider name in the accessible label', () => {
        render(<RatingBadge value={5.9} name="Internet Movie Database" />);
        expect(screen.getByLabelText('Internet Movie Database: 5.9')).toBeInTheDocument();
    });

    it('falls back to the provider name when the platform is unknown', () => {
        render(<RatingBadge value={7.2} name="Some New Ratings Plugin" />);
        expect(screen.getByText('Some New Ratings Plugin')).toBeInTheDocument();
    });

    it('uses the fresh tomato at or above 60', () => {
        const { container } = render(<RatingBadge value={94} name="Rotten Tomatoes" />);
        expect(hasFill(container, 'circle', 'var(--vora-rt-red)')).toBe(true);
    });

    it('uses the green splat below 60', () => {
        const { container } = render(<RatingBadge value={37} name="Rotten Tomatoes" />);
        expect(hasFill(container, 'path', 'var(--vora-rt-splat)')).toBe(true);
        expect(hasFill(container, 'circle', 'var(--vora-rt-red)')).toBe(false);
    });

    it('switches exactly at the 60 threshold', () => {
        const { container: fresh } = render(<RatingBadge value={60} name="Rotten Tomatoes" />);
        expect(hasFill(fresh, 'circle', 'var(--vora-rt-red)')).toBe(true);

        const { container: rotten } = render(<RatingBadge value={59} name="Rotten Tomatoes" />);
        expect(hasFill(rotten, 'circle', 'var(--vora-rt-red)')).toBe(false);
    });

    it('colours the Metacritic square by score band', () => {
        const { container: good } = render(<RatingBadge value={80} name="Metacritic" />);
        expect(hasFill(good, 'rect', 'var(--vora-metacritic-good)')).toBe(true);

        const { container: mixed } = render(<RatingBadge value={50} name="Metacritic" />);
        expect(hasFill(mixed, 'rect', 'var(--vora-metacritic-mixed)')).toBe(true);

        const { container: bad } = render(<RatingBadge value={20} name="Metacritic" />);
        expect(hasFill(bad, 'rect', 'var(--vora-metacritic-bad)')).toBe(true);
    });
});
