import { afterEach, describe, expect, it, vi } from 'vitest';
import { canOpenInBrowser, openInBrowser } from './desktopBrowser';
import type { VoraDesktopBridge } from './desktopBridge';

const bridgeWith = (openExternal?: (url: string) => Promise<boolean>) =>
    ({ bridgeVersion: 3, openExternal }) as unknown as VoraDesktopBridge;

describe('opening a page in the browser', () => {
    afterEach(() => {
        delete window.voraDesktop;
    });

    it('is not offered in a plain browser', () => {
        expect(canOpenInBrowser()).toBe(false);
    });

    it('is not offered by an older Vora Desktop that cannot open a browser', () => {
        window.voraDesktop = bridgeWith(undefined);

        expect(canOpenInBrowser()).toBe(false);
    });

    it('is offered by a Vora Desktop that can, and hands it the address', async () => {
        const openExternal = vi.fn().mockResolvedValue(true);
        window.voraDesktop = bridgeWith(openExternal);

        expect(canOpenInBrowser()).toBe(true);
        await expect(openInBrowser('https://vora.test/link?code=ABCD')).resolves.toBe(true);
        expect(openExternal).toHaveBeenCalledWith('https://vora.test/link?code=ABCD');
    });

    it('reports a refusal or a failure as not opened instead of throwing', async () => {
        window.voraDesktop = bridgeWith(vi.fn().mockResolvedValue(false));
        await expect(openInBrowser('https://vora.test/')).resolves.toBe(false);

        window.voraDesktop = bridgeWith(vi.fn().mockRejectedValue(new Error('not allowed')));
        await expect(openInBrowser('https://vora.test/')).resolves.toBe(false);

        delete window.voraDesktop;
        await expect(openInBrowser('https://vora.test/')).resolves.toBe(false);
    });
});
