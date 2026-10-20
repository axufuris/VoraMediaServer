import { afterEach, describe, expect, it, vi } from 'vitest';
import { fakeDesktopBridge } from '../test/fakeDesktopBridge';

async function freshStore() {
    vi.resetModules();
    return import('./desktopPlayback');
}

afterEach(() => {
    delete window.voraDesktop;
});

describe('the desktop bridge', () => {
    it('is absent in a browser', async () => {
        const store = await freshStore();

        await store.loadDesktopPlayback();

        expect(store.getDesktopBridge()).toBeNull();
        expect(store.isDesktopPlayback()).toBe(false);
    });

    it('is ignored when it is older than this build needs', async () => {
        window.voraDesktop = fakeDesktopBridge({ bridgeVersion: 0 }).bridge;
        const store = await freshStore();

        expect(store.getDesktopBridge()).toBeNull();
    });

    it('plays video in mpv once the settings and capabilities are read', async () => {
        window.voraDesktop = fakeDesktopBridge().bridge;
        const store = await freshStore();
        expect(store.isDesktopPlayback()).toBe(false);

        await store.loadDesktopPlayback();

        expect(store.isDesktopPlayback()).toBe(true);
        expect(store.desktopPlaybackCapabilities()?.detectedAudioChannels).toBe(8);
    });

    it('falls back to the built-in player when mpv goes missing, and back when it is found', async () => {
        const fake = fakeDesktopBridge();
        window.voraDesktop = fake.bridge;
        const store = await freshStore();
        await store.loadDesktopPlayback();

        fake.changeMpv({ available: false, version: null, reason: 'Install mpv.' });
        expect(store.isDesktopPlayback()).toBe(false);

        fake.changeMpv({ available: true, version: '0.40.0', reason: null });
        expect(store.isDesktopPlayback()).toBe(true);
    });

    it('uses the built-in player until the player closes once it is forced', async () => {
        window.voraDesktop = fakeDesktopBridge().bridge;
        const store = await freshStore();
        await store.loadDesktopPlayback();

        store.forceBuiltInPlayer();
        expect(store.isDesktopPlayback()).toBe(false);
        expect(store.desktopPlaybackCapabilities()).toBeNull();

        store.releaseBuiltInPlayer();
        expect(store.isDesktopPlayback()).toBe(true);
    });

    it('saves a settings change through the app and tells listeners', async () => {
        const fake = fakeDesktopBridge();
        window.voraDesktop = fake.bridge;
        const store = await freshStore();
        await store.loadDesktopPlayback();
        const listener = vi.fn();
        store.subscribeDesktopPlayback(listener);

        await store.updateDesktopSettings({ useMpvForVideo: false });

        expect(fake.bridge.settings.set).toHaveBeenCalledWith({ useMpvForVideo: false });
        expect(listener).toHaveBeenCalled();
        expect(store.isDesktopPlayback()).toBe(false);
    });
});
