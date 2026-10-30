import { describe, it, expect } from 'vitest';
import { thumbUrl } from './thumbnails';

describe('thumbUrl', () => {
    it('asks the server for a small copy of an image read from a title’s media folder', () => {
        const local = '/api/artwork/local/7f1c1a52-6d0f-4d0b-9c58-0c6a6f0b4c11/poster.jpg?v=638000000000000000';

        expect(thumbUrl(local, 360)).toBe(`/api/artwork/thumb?w=360&kind=poster&src=${encodeURIComponent(local)}`);
    });

    it('keeps the version in the address, so a replaced image is not served from the old copy', () => {
        const before = thumbUrl('/api/artwork/local/7f1c1a52-6d0f-4d0b-9c58-0c6a6f0b4c11/poster.jpg?v=1', 360);
        const after = thumbUrl('/api/artwork/local/7f1c1a52-6d0f-4d0b-9c58-0c6a6f0b4c11/poster.jpg?v=2', 360);

        expect(after).not.toBe(before);
    });

    it('asks for a small copy of a poster that ships with Vora', () => {
        const poster = '/api/artwork/vora/holidays/halloween.jpg?v=0123456789ab';

        expect(thumbUrl(poster, 360)).toBe(`/api/artwork/thumb?w=360&kind=poster&src=${encodeURIComponent(poster)}`);
    });

    it('does the same for uploads and for provider images', () => {
        expect(thumbUrl('/api/artwork/custom/media_1_poster.jpg', 500, 'backdrop'))
            .toBe('/api/artwork/thumb?w=500&kind=backdrop&src=%2Fapi%2Fartwork%2Fcustom%2Fmedia_1_poster.jpg');
        expect(thumbUrl('https://image.tmdb.org/t/p/w500/poster.jpg', 200))
            .toBe('/api/artwork/thumb?w=200&kind=poster&src=https%3A%2F%2Fimage.tmdb.org%2Ft%2Fp%2Fw500%2Fposter.jpg');
    });

    it('leaves anything else as it is', () => {
        expect(thumbUrl('data:image/png;base64,AAAA', 360)).toBe('data:image/png;base64,AAAA');
        expect(thumbUrl('/media/movies/Alien (1979)/poster.jpg', 360)).toBe('/media/movies/Alien (1979)/poster.jpg');
        expect(thumbUrl(null, 360)).toBeUndefined();
        expect(thumbUrl('', 360)).toBe('');
    });
});
