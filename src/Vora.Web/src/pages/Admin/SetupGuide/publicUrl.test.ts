import { describe, expect, it } from 'vitest';
import { normalizePublicUrl } from './publicUrl';

describe('normalizePublicUrl', () => {
    it('keeps a full address and drops a trailing slash', () => {
        expect(normalizePublicUrl(' https://vora.example.com/ ')).toBe('https://vora.example.com');
        expect(normalizePublicUrl('http://192.168.1.20:8080')).toBe('http://192.168.1.20:8080');
    });

    it('assumes https when the scheme is left off', () => {
        expect(normalizePublicUrl('vora.example.com')).toBe('https://vora.example.com');
        expect(normalizePublicUrl('media.tail1234.ts.net/vora')).toBe('https://media.tail1234.ts.net/vora');
    });

    it('rejects what cannot be a public web address', () => {
        expect(normalizePublicUrl('')).toBeNull();
        expect(normalizePublicUrl('not a url')).toBeNull();
        expect(normalizePublicUrl('nas')).toBeNull();
        expect(normalizePublicUrl('ftp://vora.example.com')).toBeNull();
    });
});
