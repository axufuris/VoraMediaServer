import { describe, it, expect } from 'vitest';
import { decodeJwtPayload, getProfileIdFromToken, StorageKeys } from './storageKeys';

const buildToken = (payload: Record<string, unknown>): string => {
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const body = btoa(JSON.stringify(payload))
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/, '');
    return `${header}.${body}.fake-signature`;
};

describe('decodeJwtPayload', () => {
    it('returns null for null/undefined/empty', () => {
        expect(decodeJwtPayload(null)).toBeNull();
        expect(decodeJwtPayload(undefined)).toBeNull();
        expect(decodeJwtPayload('')).toBeNull();
    });

    it('returns null for malformed token without three segments', () => {
        expect(decodeJwtPayload('not.a-jwt')).toBeNull();
        expect(decodeJwtPayload('only-one-piece')).toBeNull();
    });

    it('returns null when payload is unparseable', () => {
        expect(decodeJwtPayload('header.not-base64-json.sig')).toBeNull();
    });

    it('decodes a valid JWT payload', () => {
        const token = buildToken({ sub: 'profile-123', role: 'admin' });
        const payload = decodeJwtPayload(token);
        expect(payload).toEqual({ sub: 'profile-123', role: 'admin' });
    });

    it('handles base64url chars (- and _) in the payload', () => {
        const token = buildToken({ sub: '????+/+/' });
        const payload = decodeJwtPayload(token);
        expect(payload?.sub).toBe('????+/+/');
    });
});

describe('getProfileIdFromToken', () => {
    it('returns sub when token is valid', () => {
        const token = buildToken({ sub: 'profile-abc' });
        expect(getProfileIdFromToken(token)).toBe('profile-abc');
    });

    it('returns null when token is missing', () => {
        expect(getProfileIdFromToken(null)).toBeNull();
        expect(getProfileIdFromToken(undefined)).toBeNull();
    });

    it('returns null when sub is not a string', () => {
        const token = buildToken({ sub: 12345 });
        expect(getProfileIdFromToken(token)).toBeNull();
    });

    it('returns null when sub is absent', () => {
        const token = buildToken({ role: 'admin' });
        expect(getProfileIdFromToken(token)).toBeNull();
    });
});

describe('StorageKeys helpers', () => {
    it('produces profile-scoped spotlight key', () => {
        expect(StorageKeys.spotlight('abc')).toBe('vora_show_spotlight_abc');
    });

    it('produces profile + device scoped iptv prefs key', () => {
        expect(StorageKeys.iptvPrefs('p1', 'd1')).toBe('iptv_prefs_p1_d1');
    });
});
