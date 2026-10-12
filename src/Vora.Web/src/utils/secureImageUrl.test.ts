import { describe, expect, it } from 'vitest';
import { secureImageUrl } from './secureImageUrl';

describe('secureImageUrl', () => {
    it('asks for an http image over https on an https page', () => {
        expect(secureImageUrl('http://www.181.fm/images/logo.png', 'https:')).toBe('https://www.181.fm/images/logo.png');
        expect(secureImageUrl('HTTP://example.com/logo.png', 'https:')).toBe('https://example.com/logo.png');
    });

    it('leaves an http image alone on an http page', () => {
        expect(secureImageUrl('http://192.168.0.51:8080/logo.png', 'http:')).toBe('http://192.168.0.51:8080/logo.png');
    });

    it('leaves https, relative and data urls alone', () => {
        expect(secureImageUrl('https://example.com/logo.png', 'https:')).toBe('https://example.com/logo.png');
        expect(secureImageUrl('/api/artwork/custom/logo.png', 'https:')).toBe('/api/artwork/custom/logo.png');
        expect(secureImageUrl('data:image/png;base64,AAAA', 'https:')).toBe('data:image/png;base64,AAAA');
    });

    it('returns undefined when there is no image', () => {
        expect(secureImageUrl(null, 'https:')).toBeUndefined();
        expect(secureImageUrl(undefined, 'https:')).toBeUndefined();
        expect(secureImageUrl('', 'https:')).toBeUndefined();
    });

    it('reads the page protocol when none is given', () => {
        expect(secureImageUrl('http://example.com/logo.png')).toBe(
            window.location.protocol === 'https:' ? 'https://example.com/logo.png' : 'http://example.com/logo.png');
    });
});
