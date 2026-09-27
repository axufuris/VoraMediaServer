import { describe, it, expect } from 'vitest';
import { isFullyWatched, affectedEpisodeCount } from './watchState';

describe('isFullyWatched', () => {
    it('trusts the server for a movie', () => {
        expect(isFullyWatched({ type: 'Movie', isPlayed: true })).toBe(true);
        expect(isFullyWatched({ type: 'Movie', isPlayed: false })).toBe(false);
    });

    it('trusts the server for an episode', () => {
        expect(isFullyWatched({ type: 'Episode', isPlayed: true })).toBe(true);
    });

    it('reports a fully watched season as watched', () => {
        expect(isFullyWatched({ type: 'Season', isPlayed: true, episodes: [{}, {}] })).toBe(true);
    });

    // The regression: a show carries `seasons`, never `episodes`, so any check
    // gated on a non-empty `episodes` array reported every show as unwatched.
    it('reports a fully watched show as watched even though it has no episodes array', () => {
        expect(isFullyWatched({
            type: 'TvShow',
            isPlayed: true,
            episodes: [],
            seasons: [{ episodeCount: 20 }, { episodeCount: 26 }],
        })).toBe(true);
    });

    it('reports a partly watched show as unwatched', () => {
        expect(isFullyWatched({ type: 'TvShow', isPlayed: false, seasons: [{ episodeCount: 20 }] })).toBe(false);
    });

    it('treats a missing flag as unwatched', () => {
        expect(isFullyWatched({ type: 'Movie' })).toBe(false);
    });
});

describe('affectedEpisodeCount', () => {
    it('sums every season for a show', () => {
        expect(affectedEpisodeCount({
            type: 'TvShow',
            seasons: [{ episodeCount: 20 }, { episodeCount: 26 }],
        })).toBe(46);
    });

    it('counts a season by its episodes', () => {
        expect(affectedEpisodeCount({ type: 'Season', episodes: [{}, {}, {}] })).toBe(3);
    });

    it('returns 0 for single items, so they skip the confirmation', () => {
        expect(affectedEpisodeCount({ type: 'Movie' })).toBe(0);
        expect(affectedEpisodeCount({ type: 'Episode' })).toBe(0);
    });

    it('handles a show with no seasons loaded', () => {
        expect(affectedEpisodeCount({ type: 'TvShow' })).toBe(0);
    });

    it('ignores seasons with an unknown episode count', () => {
        expect(affectedEpisodeCount({ type: 'TvShow', seasons: [{ episodeCount: 10 }, {}] })).toBe(10);
    });
});
