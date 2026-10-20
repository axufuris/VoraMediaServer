import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { clearApiClientCache, createServerClient } from './client';
import { fakeDesktopBridge } from '../test/fakeDesktopBridge';

function headersOfARequest(): Record<string, string> {
    const instance = createServerClient();
    const config = { headers: {} as Record<string, string>, url: '/x', method: 'get' };
    // @ts-expect-error - private structure used for whitebox test
    const requestHandlers = instance.interceptors.request.handlers as { fulfilled: (c: typeof config) => typeof config }[];
    return requestHandlers[0].fulfilled(config).headers;
}

describe('the device headers', () => {
    beforeEach(() => {
        clearApiClientCache();
        localStorage.clear();
        localStorage.setItem('device_id', 'dev-1');
    });

    afterEach(() => {
        delete window.voraDesktop;
    });

    it('name the browser when there is no desktop app', () => {
        const headers = headersOfARequest();

        expect(headers['X-Vora-Client']).toBe('Vora Web');
        expect(headers['X-Vora-Device']).toBe('Web Browser');
        expect(headers['X-Vora-Device-Type']).toBe('Browser');
    });

    it('name Vora Desktop and its platform inside the desktop app', () => {
        window.voraDesktop = fakeDesktopBridge({ platform: 'windows' }).bridge;

        const headers = headersOfARequest();

        expect(headers['X-Vora-Device-Id']).toBe('dev-1');
        expect(headers['X-Vora-Client']).toBe('Vora Desktop');
        expect(headers['X-Vora-Device']).toBe('Vora Desktop (Windows)');
        expect(headers['X-Vora-Device-Type']).toBe('Desktop');
        expect(headers['X-Vora-OS']).toBe('Windows');
    });

    it('mark a QA build in the client name', () => {
        window.voraDesktop = fakeDesktopBridge({ channel: 'qa', platform: 'linux' }).bridge;

        const headers = headersOfARequest();

        expect(headers['X-Vora-Client']).toBe('Vora Desktop QA');
        expect(headers['X-Vora-Device']).toBe('Vora Desktop (Linux)');
        expect(headers['X-Vora-OS']).toBe('Linux');
    });

    it('ignore a bridge older than this build needs', () => {
        window.voraDesktop = fakeDesktopBridge({ bridgeVersion: 0 }).bridge;

        expect(headersOfARequest()['X-Vora-Client']).toBe('Vora Web');
    });
});
