import { describe, it, expect } from 'vitest';
import { albumCover } from './albumCover';

const artist = '/api/artwork/custom/artist_band.jpg';
const own = '/api/artwork/custom/album_cover.jpg';

describe('albumCover', () => {
    it('uses the album cover when it has one', () => {
        expect(albumCover({ artworkUrl: own, artistArtworkUrl: artist })).toBe(own);
    });

    it.each([undefined, '', '   '])('falls back to the artist image when the album cover is %j', (artworkUrl) => {
        expect(albumCover({ artworkUrl, artistArtworkUrl: artist })).toBe(artist);
    });

    it('returns nothing when neither has an image', () => {
        expect(albumCover({ artworkUrl: undefined, artistArtworkUrl: null })).toBeUndefined();
        expect(albumCover({ artworkUrl: '', artistArtworkUrl: ' ' })).toBeUndefined();
    });
});
