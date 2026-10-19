import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { registerServiceWorker } from './serviceWorker';

const register = vi.fn(() => Promise.resolve());

function giveBrowser({ secure, supportsWorkers }: { secure: boolean; supportsWorkers: boolean }) {
    vi.stubGlobal('isSecureContext', secure);
    if (supportsWorkers) {
        Object.defineProperty(navigator, 'serviceWorker', { value: { register }, configurable: true });
    } else {
        Reflect.deleteProperty(navigator, 'serviceWorker');
    }
}

describe('registerServiceWorker', () => {
    beforeEach(() => {
        register.mockReset().mockResolvedValue(undefined);
        vi.stubEnv('PROD', true);
    });

    afterEach(() => {
        vi.unstubAllEnvs();
        vi.unstubAllGlobals();
        Reflect.deleteProperty(navigator, 'serviceWorker');
    });

    it('registers immediately on a secure production server without waiting for page resources', () => {
        giveBrowser({ secure: true, supportsWorkers: true });

        registerServiceWorker();
        expect(register).toHaveBeenCalledWith('/sw.js');
    });

    it('registers even when the page load event has already fired', () => {
        giveBrowser({ secure: true, supportsWorkers: true });
        window.dispatchEvent(new Event('load'));

        registerServiceWorker();

        expect(register).toHaveBeenCalledWith('/sw.js');
    });

    it('reports the registration error so proxy and script failures can be diagnosed', async () => {
        giveBrowser({ secure: true, supportsWorkers: true });
        const error = new Error('The script has an unsupported MIME type');
        register.mockRejectedValueOnce(error);
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});

        registerServiceWorker();
        await Promise.resolve();

        expect(warn).toHaveBeenCalledWith(expect.stringContaining('/sw.js'), error);
        warn.mockRestore();
    });

    it('does nothing on a plain-HTTP server address, where a browser would refuse it', () => {
        giveBrowser({ secure: false, supportsWorkers: true });

        registerServiceWorker();
        window.dispatchEvent(new Event('load'));

        expect(register).not.toHaveBeenCalled();
    });

    it('does nothing in the development server or a browser without workers', () => {
        giveBrowser({ secure: true, supportsWorkers: false });
        registerServiceWorker();
        window.dispatchEvent(new Event('load'));

        giveBrowser({ secure: true, supportsWorkers: true });
        vi.stubEnv('PROD', false);
        registerServiceWorker();
        window.dispatchEvent(new Event('load'));

        expect(register).not.toHaveBeenCalled();
    });
});
