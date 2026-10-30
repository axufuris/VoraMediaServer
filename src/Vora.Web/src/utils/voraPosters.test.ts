import { describe, it, expect } from 'vitest';
import type { VoraPoster } from '../api/Media/voraPosterService';
import { isVoraPosterUrl, matchesVoraPosterSearch, suggestVoraPoster } from './voraPosters';

const poster = (id: string, category: string, title: string, aliases: string[]): VoraPoster => ({
    id, category, title, aliases, url: `/api/artwork/vora/${category}/${id}.jpg?v=1`,
});

const posters = [
    poster('halloween', 'holidays', 'Halloween', ['halloween', 'spooky season', 'spooky']),
    poster('christmas', 'holidays', 'Christmas', ['christmas', 'xmas']),
    poster('valentines-day', 'holidays', "Valentine's Day", ["valentine's", 'valentines']),
    poster('horror', 'genres', 'Horror', ['horror', 'scary']),
    poster('science-fiction', 'genres', 'Science Fiction', ['science fiction', 'sci-fi', 'sci-fi & fantasy']),
    poster('1980s', 'decades', '1980s', ['1980s', '80s', "80's", 'eighties']),
    poster('pg-13', 'ratings', 'Parents Strongly Cautioned', ['rated pg-13', 'pg-13']),
    poster('r', 'ratings', 'Restricted', ['rated r', 'r rated']),
];

const suggest = (...texts: (string | undefined)[]) => suggestVoraPoster(posters, texts)?.id;

describe('suggestVoraPoster', () => {
    it('finds the poster a collection title names', () => {
        expect(suggest('Halloween Movies')).toBe('halloween');
        expect(suggest('Xmas Classics')).toBe('christmas');
        expect(suggest('Sci-Fi Favourites')).toBe('science-fiction');
    });

    it('reads apostrophes and punctuation the way people type them', () => {
        expect(suggest('Valentines Day picks')).toBe('valentines-day');
        expect(suggest("Valentine's Day picks")).toBe('valentines-day');
        expect(suggest("Best of the '80s")).toBe('1980s');
        expect(suggest('PG-13 Movies')).toBe('pg-13');
    });

    it('prefers the longest match, so the occasion beats a genre in the same title', () => {
        expect(suggest('Christmas Horror')).toBe('christmas');
        expect(suggest('Spooky Season Horror')).toBe('halloween');
    });

    it('matches whole words only', () => {
        expect(suggest('R Kelly')).toBeUndefined();
        expect(suggest('1980s')).toBe('1980s');
        expect(suggest('1880s')).toBeUndefined();
        expect(suggest('Horrorshow Records')).toBeUndefined();
    });

    it('also reads the theme an AI Theme collection was given', () => {
        expect(suggest('Our October', 'Halloween')).toBe('halloween');
    });

    it('suggests nothing for a title it has no poster for', () => {
        expect(suggest('Marvel Cinematic Universe')).toBeUndefined();
        expect(suggest('', undefined)).toBeUndefined();
    });
});

describe('matchesVoraPosterSearch', () => {
    it('searches titles and the words that find a poster', () => {
        expect(matchesVoraPosterSearch(posters[0], 'spook')).toBe(true);
        expect(matchesVoraPosterSearch(posters[0], 'xmas')).toBe(false);
        expect(matchesVoraPosterSearch(posters[6], 'cautioned')).toBe(true);
        expect(matchesVoraPosterSearch(posters[6], '  ')).toBe(true);
    });
});

describe('isVoraPosterUrl', () => {
    it('knows a Vora poster by its address', () => {
        expect(isVoraPosterUrl('/api/artwork/vora/holidays/halloween.jpg?v=1')).toBe(true);
        expect(isVoraPosterUrl('/api/artwork/custom/coll_1.jpg')).toBe(false);
        expect(isVoraPosterUrl(undefined)).toBe(false);
    });
});
