import { describe, it, expect } from 'vitest';
import { discoveryLibraryStatus } from './discoveryLibraryStatus';

describe('discoveryLibraryStatus', () => {
    it('says in a library for a title the libraries hold', () => {
        expect(discoveryLibraryStatus(true, null)).toBe('inLibrary');
        expect(discoveryLibraryStatus(true, undefined)).toBe('inLibrary');
    });

    it('says in a library for a fulfilled request, which is a title the viewer can play', () => {
        expect(discoveryLibraryStatus(false, 'Available')).toBe('inLibrary');
    });

    it.each(['Pending', 'Approved', 'Processing'] as const)('says requested while a %s request is on its way', status => {
        expect(discoveryLibraryStatus(false, status)).toBe('requested');
        expect(discoveryLibraryStatus(undefined, status)).toBe('requested');
    });

    it('says nothing for a denied request, which promises nothing', () => {
        expect(discoveryLibraryStatus(false, 'Denied')).toBeUndefined();
    });

    it('says nothing for a title nobody has asked for', () => {
        expect(discoveryLibraryStatus(false, null)).toBeUndefined();
        expect(discoveryLibraryStatus(undefined, undefined)).toBeUndefined();
    });

    it('lets in a library win over requested', () => {
        expect(discoveryLibraryStatus(true, 'Pending')).toBe('inLibrary');
        expect(discoveryLibraryStatus(true, 'Processing')).toBe('inLibrary');
    });
});
