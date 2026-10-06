import { beforeAll, describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import DetailHero, { HeroCredits } from './DetailHero';
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

describe('DetailHero', () => {
    beforeAll(() => {
        if (typeof window.matchMedia === 'function') return;
        Object.defineProperty(window, 'matchMedia', {
            configurable: true,
            value: (query: string): MediaQueryList => ({
                matches: false,
                media: query,
                onchange: null,
                addListener: () => { },
                removeListener: () => { },
                addEventListener: () => { },
                removeEventListener: () => { },
                dispatchEvent: () => false,
            }),
        });
    });

    // The client relies on the browser's own back navigation; a Back button in
    // the hero only took space above the title.
    it('has no Back button', () => {
        render(<DetailHero title="House of the Dragon" subtitle="Season 1" />);

        expect(screen.queryByRole('button', { name: /back/i })).toBeNull();
    });

    // An episode reads show, then episode, then the numbers that label it.
    it('puts the episode title above its season and episode numbers', () => {
        const { container } = render(
            <DetailHero
                title="House of the Dragon"
                subtitle="The Heirs of the Dragon"
                titleSuffix="S1 E1"
            />,
        );

        const text = container.textContent ?? '';
        expect(text.indexOf('The Heirs of the Dragon')).toBeLessThan(text.indexOf('S1 E1'));
    });

    it('sets the season and episode line smaller than the episode title', () => {
        render(<DetailHero title="House of the Dragon" subtitle="The Heirs of the Dragon" titleSuffix="S1 E1" />);

        const episodeTitle = screen.getByText('The Heirs of the Dragon');
        const numbers = screen.getByText('S1 E1');

        expect(episodeTitle.className).toContain('text-lg');
        expect(numbers.className).toContain('text-sm');
    });

    it('centres the poster against the text beside it', () => {
        const { container } = render(<DetailHero title="House of the Dragon" posterSrc="https://example.test/poster.jpg" />);

        const grid = container.querySelector('div[class*="lg:grid-cols-"]');
        expect(grid?.className).toContain('lg:items-center');
    });

    it('draws the title treatment in place of the text when the item has one', () => {
        render(<DetailHero title="Toy Story 5" logoUrl="https://image.tmdb.org/t/p/original/logo.png" />);

        const heading = screen.getByRole('heading', { level: 1 });
        const logo = within(heading).getByRole('img', { name: 'Toy Story 5' });

        // Through the cache, in the logo bucket, so it keeps its transparency.
        expect(logo.getAttribute('src')).toContain('kind=logo');
        expect(heading).toHaveTextContent('');
    });

    it('falls back to the text title when there is no logo', () => {
        render(<DetailHero title="Toy Story 5" />);

        const heading = screen.getByRole('heading', { level: 1 });

        expect(heading).toHaveTextContent('Toy Story 5');
        expect(within(heading).queryByRole('img')).toBeNull();
    });

    it('keeps the poster to the tighter column', () => {
        const { container } = render(<DetailHero title="House of the Dragon" posterSrc="https://example.test/poster.jpg" />);

        expect(container.querySelector('[style*="max-width: 12.5rem"]')).not.toBeNull();
    });

    it('widens the column for a still rather than a poster', () => {
        const { container } = render(<DetailHero title="The Heirs of the Dragon" posterShape="still" posterSrc="https://example.test/still.jpg" />);

        expect(container.querySelector('[style*="max-width: 20rem"]')).not.toBeNull();
    });

    it('falls back to a line saying there is no overview', () => {
        render(<DetailHero title="House of the Dragon" />);

        expect(screen.getByText('No overview available.')).toBeInTheDocument();
    });
});

describe('DetailHero backdrop', () => {
    it('keeps the artwork in a 16:9 box anchored top-right instead of stretching it to the text', () => {
        render(<DetailHero title="Scrubs" backdropSrc="https://example.test/backdrop.jpg" />);

        const backdrop = screen.getByTestId('hero-backdrop');
        expect(backdrop.className).toContain('aspect-video');
        expect(backdrop.className).toContain('top-0');
        expect(backdrop.className).not.toContain('inset-y-0');
    });

    it('holds the hero open to the backdrop height, which is its 16:9 share of the width', () => {
        render(<DetailHero title="Scrubs" backdropSrc="https://example.test/backdrop.jpg" />);

        const backdrop = screen.getByTestId('hero-backdrop').className;
        const spacer = screen.getByTestId('hero-backdrop-spacer').className;

        expect(backdrop).toContain('lg:w-[min(64%,calc(70vh*16/9))]');
        expect(spacer).toContain('lg:pt-[min(36%,70vh)]');
    });

    it('stacks the poster over the text until the page is wide enough for both side by side', () => {
        const { container } = render(<DetailHero title="Scrubs" posterShape="still" posterSrc="https://example.test/still.jpg" />);

        const grid = container.querySelector('div[class*="grid-cols-"]');

        expect(grid?.className).not.toMatch(/(^|\s)md:grid-cols-/);
        expect(grid?.className).toContain('lg:grid-cols-[16rem_1fr]');
        expect(grid?.className).toContain('xl:grid-cols-[20rem_1fr]');
    });

    it('shares one grid cell between the spacer and the content so the content centres against the artwork', () => {
        render(<DetailHero title="Scrubs" backdropSrc="https://example.test/backdrop.jpg" />);

        const spacer = screen.getByTestId('hero-backdrop-spacer');
        const content = screen.getByRole('heading', { level: 1 }).closest('div.flex');

        expect(spacer.className).toContain('col-start-1 row-start-1');
        expect(content?.className).toContain('col-start-1 row-start-1');
        expect(content?.className).toContain('justify-center');
    });
});

describe('DetailHero tech chips', () => {
    it('renders the chips in their own corner group', () => {
        render(<DetailHero title="Idiots" techChips={<span>1080p</span>} />);

        expect(screen.getByTestId('hero-tech-chips')).toHaveTextContent('1080p');
    });

    it('keeps room under the text for the corner chips so they never sit on the overview', () => {
        render(<DetailHero title="Idiots" techChips={<span>1080p</span>} />);

        const content = screen.getByRole('heading', { level: 1 }).closest('div.flex');
        expect(content?.className).toContain('lg:pb-16');
    });

    it('renders no group when there are none', () => {
        render(<DetailHero title="Idiots" />);

        expect(screen.queryByTestId('hero-tech-chips')).toBeNull();
    });
});
