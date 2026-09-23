import { describe, it, expect } from 'vitest';
import { libraryTypeToName } from './libraryTypes';

// The previous mapping fell through to 'Movie' for anything it did not
// recognise, so a Live TV library would have been told the film metadata
// providers applied to it. A wrong answer is worse than no answer here: the
// caller sends this to the server to narrow a provider list, and 'Movie' is a
// confident claim while undefined just means "no kind stated".
describe('libraryTypeToName', () => {
    it.each([
        [1, 'Movie'],
        [2, 'TvShow'],
        [3, 'Music'],
        [4, 'HomeVideo'],
        [5, 'LiveTv'],
    ])('maps %i to %s', (type, expected) => {
        expect(libraryTypeToName(type)).toBe(expected);
    });

    it('does not claim Movie for an unknown type', () => {
        expect(libraryTypeToName(99)).toBeUndefined();
    });

    it('does not claim Movie for zero', () => {
        expect(libraryTypeToName(0)).toBeUndefined();
    });

    // LiveTv exists as a library type but no plugin declares it, so it resolves
    // to a name that simply matches nothing — which is the correct outcome.
    it('names LiveTv rather than falling back to a video type', () => {
        expect(libraryTypeToName(5)).toBe('LiveTv');
        expect(libraryTypeToName(5)).not.toBe('Movie');
    });
});
