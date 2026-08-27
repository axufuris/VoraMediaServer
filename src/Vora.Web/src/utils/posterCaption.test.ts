import { describe, it, expect } from 'vitest';
import { posterCaption } from './posterCaption';

describe('posterCaption', () => {
    it('movie: title, then year · edition', () => {
        const cap = posterCaption({ type: 'Movie', title: 'Blade Runner', releaseDate: '1982-06-25', edition: "Director's Cut" });
        expect(cap.title).toBe('Blade Runner');
        expect(cap.lines).toEqual(["1982 · Director's Cut"]);
    });

    it('movie without edition: just the year', () => {
        const cap = posterCaption({ type: 'Movie', title: 'Dune', releaseDate: '2021-10-22' });
        expect(cap.lines).toEqual(['2021']);
    });

    it('tv show: title then year', () => {
        const cap = posterCaption({ type: 'TvShow', title: 'Severance', releaseDate: '2022-02-18' });
        expect(cap.title).toBe('Severance');
        expect(cap.lines).toEqual(['2022']);
    });

    it('season: show name, Season N, year', () => {
        const cap = posterCaption({ type: 'Season', title: 'Season 5', tvShowTitle: 'Jersey Shore: Family Vacation', seasonNumber: 5, seasonName: 'Season 5', releaseDate: '2026-01-01' });
        expect(cap.title).toBe('Jersey Shore: Family Vacation');
        expect(cap.lines).toEqual(['Season 5', '2026']);
    });

    it('season with a real name shows the name', () => {
        const cap = posterCaption({ type: 'Season', title: 'Specials', tvShowTitle: 'Doctor Who', seasonNumber: 0, seasonName: 'Specials' });
        expect(cap.lines[0]).toBe('Specials');
    });

    it('episode: show, S#E#, episode title', () => {
        const cap = posterCaption({ type: 'Episode', title: 'We Light the Way', tvShowTitle: 'House of the Dragon', seasonNumber: 1, seasonName: 'Season 1', episodeNumber: 5 });
        expect(cap.title).toBe('House of the Dragon');
        expect(cap.lines).toEqual(['We Light the Way', 'S1 · E5']);
    });

    it('episode: named season without a number falls back to label · E#', () => {
        const cap = posterCaption({ type: 'Episode', title: 'The Snowmen', tvShowTitle: 'Doctor Who', seasonName: 'Specials', episodeNumber: 3 });
        expect(cap.lines).toEqual(['The Snowmen', 'Specials · E3']);
    });

    it('drops empty parts (no year, no edition)', () => {
        const cap = posterCaption({ type: 'Movie', title: 'Untitled' });
        expect(cap.lines).toEqual([]);
    });
});
