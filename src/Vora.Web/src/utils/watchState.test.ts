import { describe, it, expect } from 'vitest';
import { isFullyWatched, affectedEpisodeCount, changedEpisodeCount } from './watchState';

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

describe('changedEpisodeCount', () => {
    const seasonOfEight = {
        type: 'Season',
        episodes: [...Array.from({ length: 7 }, () => ({ isPlayed: true })), { isPlayed: false }],
    };

    it('counts only the season episodes a toggle changes', () => {
        expect(changedEpisodeCount(seasonOfEight, true)).toBe(1);
        expect(changedEpisodeCount(seasonOfEight, false)).toBe(7);
    });

    it('counts a show by its episodes left', () => {
        const show = { type: 'TvShow', unplayedItemCount: 2, seasons: [{ episodeCount: 10 }, { episodeCount: 12 }] };

        expect(changedEpisodeCount(show, true)).toBe(2);
        expect(changedEpisodeCount(show, false)).toBe(20);
    });

    it('falls back to every episode when the show has no count', () => {
        expect(changedEpisodeCount({ type: 'TvShow', seasons: [{ episodeCount: 10 }, { episodeCount: 12 }] }, true)).toBe(22);
    });

    it('falls back to every episode when the count does not fit', () => {
        expect(changedEpisodeCount({ type: 'TvShow', unplayedItemCount: 0, seasons: [{ episodeCount: 10 }] }, true)).toBe(10);
        expect(changedEpisodeCount({ type: 'TvShow', unplayedItemCount: 40, seasons: [{ episodeCount: 10 }] }, true)).toBe(10);
        expect(changedEpisodeCount({ type: 'Season', episodes: [{ isPlayed: true }, { isPlayed: true }] }, true)).toBe(2);
    });

    it('treats an episode with no state as unwatched', () => {
        expect(changedEpisodeCount({ type: 'Season', episodes: [{}, { isPlayed: true }, {}] }, true)).toBe(2);
    });
});
