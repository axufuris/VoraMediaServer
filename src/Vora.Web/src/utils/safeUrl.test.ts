import { describe, expect, it } from 'vitest';
import { safeMediaUrl } from './safeUrl';

describe('safeMediaUrl', () => {
    it('keeps http, https and blob urls', () => {
        expect(safeMediaUrl('https://image.tmdb.org/t/p/w500/a.jpg')).toBe('https://image.tmdb.org/t/p/w500/a.jpg');
        expect(safeMediaUrl('http://192.168.0.51:8080/api/stream.m3u8')).toBe('http://192.168.0.51:8080/api/stream.m3u8');
        expect(safeMediaUrl('blob:http://localhost/1234')).toBe('blob:http://localhost/1234');
    });

    it('resolves relative paths against the page origin', () => {
        expect(safeMediaUrl('/api/artwork/thumb?w=300')).toBe(`${window.location.origin}/api/artwork/thumb?w=300`);
    });

    it('rejects script and data urls', () => {
        expect(safeMediaUrl('javascript:alert(1)')).toBeUndefined();
        expect(safeMediaUrl(' JavaScript:alert(1)')).toBeUndefined();
        expect(safeMediaUrl('data:text/html,<script>alert(1)</script>')).toBeUndefined();
    });

    it('returns undefined for empty input', () => {
        expect(safeMediaUrl('')).toBeUndefined();
        expect(safeMediaUrl('   ')).toBeUndefined();
        expect(safeMediaUrl(null)).toBeUndefined();
    });
});
