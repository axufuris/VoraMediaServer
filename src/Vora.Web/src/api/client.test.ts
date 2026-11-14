import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createServerClient, clearApiClientCache, getResponseStatus, profileIdForServer } from './client';
import { serverVault, type VoraServer } from '../utils/serverVault';
import { getProfileIdFromToken } from '../utils/storageKeys';

describe('getResponseStatus', () => {
    it('returns the response status for axios errors', () => {
        const axiosError = {
            isAxiosError: true,
            response: { status: 401 },
            name: 'AxiosError',
            message: 'Unauthorized',
            toJSON: () => ({}),
        };
        expect(getResponseStatus(axiosError)).toBe(401);
    });

    it('returns undefined for axios errors without a response', () => {
        const axiosError = {
            isAxiosError: true,
            name: 'AxiosError',
            message: 'Network Error',
            toJSON: () => ({}),
        };
        expect(getResponseStatus(axiosError)).toBeUndefined();
    });

    it('returns undefined for non-axios errors', () => {
        expect(getResponseStatus(new Error('plain'))).toBeUndefined();
        expect(getResponseStatus('string error')).toBeUndefined();
        expect(getResponseStatus(null)).toBeUndefined();
    });
});

describe('createServerClient caching', () => {
    beforeEach(() => {
        clearApiClientCache();
        localStorage.clear();
        sessionStorage.clear();
    });

    afterEach(() => {
        clearApiClientCache();
    });

    it('returns the same instance for repeated calls with the same serverId', () => {
        serverVault.addOrUpdateServer({
            id: 'server-a',
            name: 'A',
            url: 'http://localhost:5000',
            token: 'token-a',
            profileId: 'profile-a',
            isAdmin: false,
        });

        const first = createServerClient('server-a');
        const second = createServerClient('server-a');

        expect(first).toBe(second);
    });

    it('returns different instances for different serverIds', () => {
        serverVault.addOrUpdateServer({
            id: 'server-a',
            name: 'A',
            url: 'http://a',
            token: 'ta',
            profileId: 'pa',
            isAdmin: false,
        });
        serverVault.addOrUpdateServer({
            id: 'server-b',
            name: 'B',
            url: 'http://b',
            token: 'tb',
            profileId: 'pb',
            isAdmin: false,
        });

        const a = createServerClient('server-a');
        const b = createServerClient('server-b');

        expect(a).not.toBe(b);
        expect(a.defaults.baseURL).toBe('http://a/api');
        expect(b.defaults.baseURL).toBe('http://b/api');
    });

    it('clearApiClientCache forces a fresh instance on next call', () => {
        serverVault.addOrUpdateServer({
            id: 'server-a',
            name: 'A',
            url: 'http://localhost:5000',
            token: 'token-a',
            profileId: 'profile-a',
            isAdmin: false,
        });

        const first = createServerClient('server-a');
        clearApiClientCache();
        const afterClear = createServerClient('server-a');

        expect(afterClear).not.toBe(first);
    });
});

describe('createServerClient request interceptor', () => {
    beforeEach(() => {
        clearApiClientCache();
        localStorage.clear();
        sessionStorage.clear();
    });

    it('attaches a Bearer token from the server vault when serverId is provided', async () => {
        serverVault.addOrUpdateServer({
            id: 'server-c',
            name: 'C',
            url: 'http://c',
            token: 'vault-token',
            profileId: 'pc',
            isAdmin: false,
        });
        localStorage.setItem('device_id', 'dev-1');

        const instance = createServerClient('server-c');

        const config = { headers: {} as Record<string, string>, url: '/x', method: 'get' };
        // axios v1 stores interceptors on `.handlers`; invoke the registered fulfilled handler.
        // @ts-expect-error - private structure used for whitebox test
        const requestHandlers = instance.interceptors.request.handlers as { fulfilled: (c: typeof config) => typeof config }[];
        const fulfilled = requestHandlers[0].fulfilled;

        const result = fulfilled(config);

        expect(result.headers['Authorization']).toBe('Bearer vault-token');
        expect(result.headers['X-Vora-Device-Id']).toBe('dev-1');
        expect(result.headers['X-Vora-Client']).toBe('Vora Web');
    });

    it('falls back to localStorage profile token when no server vault entry', async () => {
        localStorage.setItem('profile_token', 'fallback-token');
        localStorage.setItem('device_id', 'dev-1');

        const instance = createServerClient();

        const config = { headers: {} as Record<string, string>, url: '/x', method: 'get' };
        // @ts-expect-error - private structure used for whitebox test
        const requestHandlers = instance.interceptors.request.handlers as { fulfilled: (c: typeof config) => typeof config }[];
        const result = requestHandlers[0].fulfilled(config);

        expect(result.headers['Authorization']).toBe('Bearer fallback-token');
    });

    it('strips Authorization when token literal is the string "undefined"', async () => {
        // serverVault entry with no real token but device id present
        localStorage.setItem('profile_token', 'undefined');
        localStorage.setItem('device_id', 'dev-1');

        const instance = createServerClient();
        const config = { headers: { Authorization: 'Bearer leftover' } as Record<string, string>, url: '/x', method: 'get' };
        // @ts-expect-error - private structure used for whitebox test
        const requestHandlers = instance.interceptors.request.handlers as { fulfilled: (c: typeof config) => typeof config }[];
        const result = requestHandlers[0].fulfilled(config);

        expect(result.headers['Authorization']).toBeUndefined();
    });
});

describe('createServerClient response interceptor 401 handling', () => {
    beforeEach(() => {
        clearApiClientCache();
        localStorage.clear();
        sessionStorage.clear();
        // JSDOM defaults pathname to '/' which is not '/login', so the redirect block
        // would call window.location.assign. We stub it via vi.stubGlobal to avoid the
        // non-configurable property restriction on window.location.
        vi.stubGlobal('location', { ...window.location, pathname: '/dashboard', assign: vi.fn() });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('clears auth-related localStorage and redirects to /login on 401', async () => {
        localStorage.setItem('profile_token', 'will-be-cleared');
        localStorage.setItem('account_token', 'will-be-cleared');
        localStorage.setItem('is_server_admin', 'true');
        localStorage.setItem('is_profile_admin', 'true');

        const instance = createServerClient();
        // @ts-expect-error - private structure used for whitebox test
        const responseHandlers = instance.interceptors.response.handlers as { rejected: (err: unknown) => unknown }[];
        const rejected = responseHandlers[0].rejected;

        const fakeAxiosError = {
            isAxiosError: true,
            response: { status: 401 },
            name: 'AxiosError',
            message: 'Unauthorized',
            toJSON: () => ({}),
        };

        await expect(rejected(fakeAxiosError)).rejects.toBe(fakeAxiosError);

        expect(localStorage.getItem('profile_token')).toBeNull();
        expect(localStorage.getItem('account_token')).toBeNull();
        expect(localStorage.getItem('is_server_admin')).toBeNull();
        expect(localStorage.getItem('is_profile_admin')).toBeNull();
    });

    it('does not clear storage on non-401 errors', async () => {
        localStorage.setItem('profile_token', 'keep-me');

        const instance = createServerClient();
        // @ts-expect-error - private structure used for whitebox test
        const responseHandlers = instance.interceptors.response.handlers as { rejected: (err: unknown) => unknown }[];
        const rejected = responseHandlers[0].rejected;

        const fakeAxiosError = {
            isAxiosError: true,
            response: { status: 500 },
            name: 'AxiosError',
            message: 'Server Error',
            toJSON: () => ({}),
        };

        await expect(rejected(fakeAxiosError)).rejects.toBe(fakeAxiosError);

        expect(localStorage.getItem('profile_token')).toBe('keep-me');
    });
});

describe('profileIdForServer', () => {
    const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

    const vaultServer = (id: string, token: string, profileId: string): VoraServer =>
        ({ id, name: id, url: `http://${id}`, token, profileId, isAdmin: false });

    const sentToken = (serverId?: string): string | undefined => {
        const config = { headers: {} as Record<string, string>, url: '/x', method: 'get' };
        // @ts-expect-error - private structure used for whitebox test
        const requestHandlers = createServerClient(serverId).interceptors.request.handlers as { fulfilled: (c: typeof config) => typeof config }[];
        const authorization = requestHandlers[0].fulfilled(config).headers['Authorization'];
        return authorization?.replace('Bearer ', '');
    };

    beforeEach(() => {
        clearApiClientCache();
        localStorage.clear();
        sessionStorage.clear();
        localStorage.setItem('profile_token', tokenFor('primary-profile'));
    });

    afterEach(() => {
        clearApiClientCache();
    });

    it('names the profile in the vault token of the server the request goes to, not the primary one', () => {
        serverVault.addOrUpdateServer(vaultServer('home', tokenFor('primary-profile'), 'primary-profile'));
        serverVault.addOrUpdateServer(vaultServer('second', tokenFor('second-profile'), 'second-profile'));
        serverVault.setActiveServerId('home');

        expect(profileIdForServer('second')).toBe('second-profile');
        expect(getProfileIdFromToken(sentToken('second'))).toBe('second-profile');
        expect(profileIdForServer('home')).toBe('primary-profile');
    });

    it('names the active vault server profile when no server is given', () => {
        serverVault.addOrUpdateServer(vaultServer('home', tokenFor('primary-profile'), 'primary-profile'));
        serverVault.addOrUpdateServer(vaultServer('second', tokenFor('second-profile'), 'second-profile'));
        serverVault.setActiveServerId('second');

        expect(profileIdForServer()).toBe('second-profile');
        expect(getProfileIdFromToken(sentToken())).toBe('second-profile');
    });

    it('names the primary profile when there is no vault at all, as today', () => {
        expect(profileIdForServer()).toBe('primary-profile');
        expect(profileIdForServer('unknown-server')).toBe('primary-profile');
        expect(getProfileIdFromToken(sentToken('unknown-server'))).toBe('primary-profile');
    });

    it('names the pending token profile while signing in to a new server', () => {
        serverVault.addOrUpdateServer(vaultServer('home', tokenFor('primary-profile'), 'primary-profile'));
        serverVault.setActiveServerId('home');
        sessionStorage.setItem('pending_server_url', 'http://new');
        sessionStorage.setItem('pending_user_token', tokenFor('pending-profile'));

        expect(profileIdForServer()).toBe('pending-profile');
        expect(getProfileIdFromToken(sentToken())).toBe('pending-profile');
    });

    it('prefers the pending token to the stored profile token when no vault server matches', () => {
        sessionStorage.setItem('pending_user_token', tokenFor('pending-profile'));

        expect(profileIdForServer()).toBe('pending-profile');
        expect(getProfileIdFromToken(sentToken())).toBe('pending-profile');
    });

    it('falls back to the vault profileId when the token carries no profile', () => {
        serverVault.addOrUpdateServer(vaultServer('second', 'opaque-token', 'second-profile'));

        expect(profileIdForServer('second')).toBe('second-profile');
        expect(sentToken('second')).toBe('opaque-token');
    });

    it('is null when no token names a profile and the vault has none', () => {
        localStorage.removeItem('profile_token');

        expect(profileIdForServer()).toBeNull();
    });

    it('keeps naming the profile the request carries after the vault token changes', () => {
        serverVault.addOrUpdateServer(vaultServer('home', tokenFor('old-profile'), 'old-profile'));
        serverVault.setActiveServerId('home');
        expect(getProfileIdFromToken(sentToken())).toBe('old-profile');

        serverVault.addOrUpdateServer(vaultServer('home', tokenFor('new-profile'), 'new-profile'));

        expect(profileIdForServer()).toBe('new-profile');
        expect(getProfileIdFromToken(sentToken())).toBe('new-profile');
    });
});
