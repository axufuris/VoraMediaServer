import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { HeroCredits } from './DetailHero';
import { directorsFrom } from '../../../utils/credits';

describe('HeroCredits', () => {
    it('renders nothing when there is no credit to show', () => {
        const { container } = render(<HeroCredits />);
        expect(container.firstChild).toBeNull();
    });

    it('lists every name when within the cap', () => {
        render(<HeroCredits directors={['John Singleton', 'Brian Moon']} />);
        expect(screen.getByText('John Singleton, Brian Moon')).toBeInTheDocument();
    });

    it('caps long credit lists and counts the remainder', () => {
        render(<HeroCredits directors={['A', 'B', 'C', 'D', 'E']} />);
        expect(screen.getByText('A, B, C +2 more')).toBeInTheDocument();
    });

    it('keeps the full list available as a tooltip when capped', () => {
        render(<HeroCredits directors={['A', 'B', 'C', 'D']} />);
        expect(screen.getByTitle('A, B, C, D')).toBeInTheDocument();
    });

    it('singularises the label for one name', () => {
        render(<HeroCredits directors={['John Singleton']} studios={['Universal']} />);
        expect(screen.getByText('Director')).toBeInTheDocument();
        expect(screen.getByText('Studio')).toBeInTheDocument();
    });

    it('pluralises the label for several names', () => {
        render(<HeroCredits directors={['A', 'B']} studios={['X', 'Y']} />);
        expect(screen.getByText('Directors')).toBeInTheDocument();
        expect(screen.getByText('Studios')).toBeInTheDocument();
    });

    it('does not cap genres, which are short and all meaningful', () => {
        render(<HeroCredits genres={['Action', 'Crime', 'Thriller', 'Drama']} />);
        expect(screen.getByText('Action, Crime, Thriller, Drama')).toBeInTheDocument();
    });
});

describe('directorsFrom', () => {
    it('picks directing credits out of a mixed cast list', () => {
        const cast = [
            { name: 'Paul Walker', role: 'Actor' },
            { name: 'John Singleton', role: 'Director' },
            { name: 'Michael Brandt', role: 'Writer' },
        ];
        expect(directorsFrom(cast)).toEqual(['John Singleton']);
    });

    it('matches a combined credit', () => {
        const cast = [{ name: 'Someone', role: 'Director, Producer' }];
        expect(directorsFrom(cast)).toEqual(['Someone']);
    });

    it('returns an empty list for no cast', () => {
        expect(directorsFrom(undefined)).toEqual([]);
    });
});
