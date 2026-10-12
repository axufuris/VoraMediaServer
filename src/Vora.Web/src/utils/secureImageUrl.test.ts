import { describe, expect, it } from 'vitest';
import { secureImageUrl } from './secureImageUrl';

describe('secureImageUrl', () => {
    it('asks the server for an http image on an https page', () => {
        expect(secureImageUrl('http://www.181.fm/images/logo.png', 'logo', 'https:'))
            .toBe('/api/artwork/thumb?w=500&kind=logo&src=http%3A%2F%2Fwww.181.fm%2Fimages%2Flogo.png');
        expect(secureImageUrl('HTTP://example.com/logo.png', 'logo', 'https:'))
            .toBe('/api/artwork/thumb?w=500&kind=logo&src=HTTP%3A%2F%2Fexample.com%2Flogo.png');
    });

    it('asks for podcast artwork as a poster, so it is not kept as a png', () => {
        expect(secureImageUrl('http://feeds.example.com/cover.jpg', 'poster', 'https:'))
            .toBe('/api/artwork/thumb?w=500&kind=poster&src=http%3A%2F%2Ffeeds.example.com%2Fcover.jpg');
    });

    it('leaves an http image alone on an http page', () => {
        expect(secureImageUrl('http://192.168.0.51:8080/logo.png', 'logo', 'http:')).toBe('http://192.168.0.51:8080/logo.png');
    });

    it('leaves https, relative and data urls alone', () => {
        expect(secureImageUrl('https://example.com/logo.png', 'logo', 'https:')).toBe('https://example.com/logo.png');
        expect(secureImageUrl('/api/artwork/custom/logo.png', 'logo', 'https:')).toBe('/api/artwork/custom/logo.png');
        expect(secureImageUrl('data:image/png;base64,AAAA', 'logo', 'https:')).toBe('data:image/png;base64,AAAA');
    });

    it('returns undefined when there is no image', () => {
        expect(secureImageUrl(null, 'logo', 'https:')).toBeUndefined();
        expect(secureImageUrl(undefined, 'logo', 'https:')).toBeUndefined();
        expect(secureImageUrl('', 'logo', 'https:')).toBeUndefined();
    });

    it('reads the page protocol when none is given', () => {
        expect(secureImageUrl('http://example.com/logo.png')).toBe(
            window.location.protocol === 'https:'
                ? '/api/artwork/thumb?w=500&kind=logo&src=http%3A%2F%2Fexample.com%2Flogo.png'
                : 'http://example.com/logo.png');
    });
});
