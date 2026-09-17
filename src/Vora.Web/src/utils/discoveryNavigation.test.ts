import { describe, it, expect } from 'vitest';
import { discoveryTarget } from './discoveryNavigation';

// A discovery result the server recognised as already-owned carries
// `mediaItemId`. Ignoring it opened the "not in your library" page for something
// in the viewer's library: no Play button, no watch state, just Add to Watchlist
// for a film they already have.
const external = { providerId: 'tmdb_discovery', type: 'Movie', externalId: '1368337' };
const owned = { ...external, mediaItemId: 'bfd5ad0d-f2d4-4bbc-a84b-251846fb2178' };

describe('discovery navigation target', () => {
    it('opens the library page for a title the server already holds', () => {
        expect(discoveryTarget(owned)).toBe('/media/bfd5ad0d-f2d4-4bbc-a84b-251846fb2178');
    });

    it('opens the discovery page for a title that is not held', () => {
        expect(discoveryTarget(external)).toBe('/discovery/tmdb_discovery/Movie/1368337');
    });

    // Multi-server sessions address everything under the server they came from,
    // and a link that drops the prefix lands on the wrong server's item.
    it('keeps the server prefix on a library target', () => {
        expect(discoveryTarget(owned, 'srv1')).toBe('/server/srv1/media/bfd5ad0d-f2d4-4bbc-a84b-251846fb2178');
    });

    it('keeps the server prefix on a discovery target', () => {
        expect(discoveryTarget(external, 'srv1')).toBe('/server/srv1/discovery/tmdb_discovery/Movie/1368337');
    });

    // The field is optional on the wire, and an absent one has to read the same
    // as a null rather than producing "/media/undefined".
    it.each([null, undefined, ''])('treats %s as not held', mediaItemId => {
        expect(discoveryTarget({ ...external, mediaItemId })).toContain('/discovery/');
    });
});
