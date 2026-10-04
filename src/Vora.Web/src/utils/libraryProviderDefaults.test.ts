import { describe, expect, it } from 'vitest';
import { PROVIDER_PREFERENCES, keepOrPick } from './libraryProviderDefaults';

const options = (...ids: string[]) => ids.map(id => ({ id }));

describe('library provider defaults', () => {
    it('keeps a default whose plugin is set up', () => {
        expect(keepOrPick('omdb_imdb', options('omdb_imdb', 'tmdb_rating'), PROVIDER_PREFERENCES[1].rating1)).toBe('omdb_imdb');
    });

    it('falls back to a provider that has a key', () => {
        expect(keepOrPick('omdb_imdb', options('tmdb_rating'), PROVIDER_PREFERENCES[1].rating1)).toBe('tmdb_rating');
        expect(keepOrPick('tvdb_metadata', options('tmdb_metadata'), PROVIDER_PREFERENCES[2].metadata)).toBe('tmdb_metadata');
    });

    it('leaves the second rating empty rather than repeating the first', () => {
        expect(keepOrPick('omdb_rotten_tomatoes', options('tmdb_rating'), PROVIDER_PREFERENCES[1].rating2, 'tmdb_rating')).toBe('');
    });

    it('respects None', () => {
        expect(keepOrPick('', options('omdb_imdb'), PROVIDER_PREFERENCES[1].rating1)).toBe('');
    });
});
