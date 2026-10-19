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
        register.mockClear();
        vi.stubEnv('PROD', true);
    });

    afterEach(() => {
        vi.unstubAllEnvs();
        vi.unstubAllGlobals();
        Reflect.deleteProperty(navigator, 'serviceWorker');
    });

    it('registers the worker once the page has loaded on a secure server', () => {
        giveBrowser({ secure: true, supportsWorkers: true });

        registerServiceWorker();
        expect(register).not.toHaveBeenCalled();
        window.dispatchEvent(new Event('load'));

        expect(register).toHaveBeenCalledWith('/sw.js');
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
