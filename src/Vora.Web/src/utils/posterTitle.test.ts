import { describe, it, expect } from 'vitest';
import { posterTitle } from './posterTitle';

describe('posterTitle', () => {
    it('prefixes the show name for a season', () => {
        expect(posterTitle({ type: 'Season', title: 'Season 2', tvShowTitle: 'Loki' })).toBe('Loki: Season 2');
    });

    it('keeps a custom season name after the show name', () => {
        expect(posterTitle({ type: 'Season', title: 'The Final Season', tvShowTitle: 'Attack on Titan' }))
            .toBe('Attack on Titan: The Final Season');
    });

    it('falls back to the plain title for a season with no show title', () => {
        expect(posterTitle({ type: 'Season', title: 'Season 1' })).toBe('Season 1');
    });

    it('leaves movies and shows untouched', () => {
        expect(posterTitle({ type: 'Movie', title: 'Iron Man', tvShowTitle: 'X' })).toBe('Iron Man');
        expect(posterTitle({ type: 'TvShow', title: 'Loki' })).toBe('Loki');
    });
});
