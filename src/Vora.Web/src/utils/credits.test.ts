import { describe, it, expect } from 'vitest';
import { newestFirst } from './credits';

describe('newestFirst', () => {
    it('orders by release date, newest first, down to the day', () => {
        const credits = [
            { title: 'Totally Killer', releaseDate: '2023-10-06' },
            { title: 'Idiots', releaseDate: '2026-03-20T00:00:00Z' },
            { title: 'Twisters', releaseDate: '2024-07-19' },
            { title: 'Red One', releaseDate: '2024-11-15' },
        ];

        expect(newestFirst(credits).map(c => c.title)).toEqual(['Idiots', 'Red One', 'Twisters', 'Totally Killer']);
    });

    it('falls back to the year, and puts undated credits last in their original order', () => {
        const credits = [
            { title: 'Undated A' },
            { title: 'Mad Men', year: 2007 },
            { title: 'Undated B', releaseDate: 'not a date' },
            { title: 'Sabrina', releaseDate: '2018-10-26', year: 2018 },
        ];

        expect(newestFirst(credits).map(c => c.title)).toEqual(['Sabrina', 'Mad Men', 'Undated A', 'Undated B']);
    });

    it('does not reorder the list it was given', () => {
        const credits = [{ title: 'Old', year: 2000 }, { title: 'New', year: 2020 }];
        newestFirst(credits);
        expect(credits[0].title).toBe('Old');
    });
});
