import { describe, it, expect } from 'vitest';
import { artworkKindQuery } from './artworkService';

// The endpoints bind `[FromQuery] ArtworkKind kind`. Sending `type=` left the
// required parameter unbound and every upload and add-from-URL was rejected
// with a bare 400 before the server ran any code. ArtworkUrlEndpointContractTests
// pins the server side of the same name.
describe('artwork kind query', () => {
    it.each(['Poster', 'Backdrop'] as const)('sends %s under the name the API binds', kind => {
        expect(artworkKindQuery(kind)).toBe(`kind=${kind}`);
    });

    it('does not use the name the API ignores', () => {
        expect(artworkKindQuery('Poster')).not.toContain('type=');
    });
});
