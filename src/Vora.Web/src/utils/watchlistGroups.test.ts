import { describe, it, expect } from 'vitest';
import type { WatchlistItem } from '../api/Watchlist/watchlistService';
import { groupWatchlist } from './watchlistGroups';

const item = (id: string, type: string, mediaItemId?: string): WatchlistItem => ({
    id, profileId: 'p', externalId: id, providerId: 'tmdb_discovery', type, title: id, addedAt: '2026-11-15T00:00:00Z', mediaItemId,
});

const ids = (items: WatchlistItem[]) => items.map(i => i.id);

describe('groupWatchlist', () => {
    it('orders the groups movies in the library, shows in the library, movies not yet in it, shows not yet in it', () => {
        const groups = groupWatchlist([
            item('show-wanted', 'TvShow'),
            item('movie-wanted', 'Movie'),
            item('show-here', 'TvShow', 'lib-1'),
            item('movie-here', 'Movie', 'lib-2'),
        ]);

        expect(groups.map(g => g.title)).toEqual([
            'Movies in Library',
            'Shows in Library',
            'Movies Not Yet in Library',
            'Shows Not Yet in Library',
        ]);
        expect(groups.map(g => ids(g.items))).toEqual([['movie-here'], ['show-here'], ['movie-wanted'], ['show-wanted']]);
    });

    it('leaves out a group with nothing in it', () => {
        const groups = groupWatchlist([item('a', 'Movie', 'lib-1'), item('b', 'TvShow')]);

        expect(groups.map(g => g.key)).toEqual(['moviesInLibrary', 'showsNotInLibrary']);
    });

    it("keeps the server's order inside a group", () => {
        const groups = groupWatchlist([item('newest', 'Movie', 'l1'), item('middle', 'Movie', 'l2'), item('oldest', 'Movie', 'l3')]);

        expect(ids(groups[0].items)).toEqual(['newest', 'middle', 'oldest']);
    });

    it('reads the type without caring about case, and counts anything that is not a show as a movie', () => {
        const groups = groupWatchlist([item('a', 'tvshow'), item('b', 'TVSHOW'), item('c', 'Season'), item('d', '')]);

        expect(groups.map(g => [g.key, ids(g.items)])).toEqual([
            ['moviesNotInLibrary', ['c', 'd']],
            ['showsNotInLibrary', ['a', 'b']],
        ]);
    });

    it('is empty for an empty watchlist', () => {
        expect(groupWatchlist([])).toEqual([]);
    });
});
