import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { INSTALLED_APP_MEDIA_QUERY, useIsInstalledApp } from './useIsInstalledApp';

type Listener = () => void;

const stubMatchMedia = (initial: boolean) => {
    const listeners = new Set<Listener>();
    const query = {
        matches: initial,
        addEventListener: (_: string, listener: Listener) => listeners.add(listener),
        removeEventListener: (_: string, listener: Listener) => listeners.delete(listener),
    };
    const matchMedia = vi.fn(() => query);
    vi.stubGlobal('matchMedia', matchMedia);
    return {
        matchMedia,
        change: (matches: boolean) => {
            query.matches = matches;
            listeners.forEach(listener => listener());
        },
    };
};

describe('useIsInstalledApp', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        delete window.voraDesktop;
        Reflect.deleteProperty(navigator, 'standalone');
    });

    it('is false in a browser tab, and where the browser has no matchMedia', () => {
        stubMatchMedia(false);
        expect(renderHook(() => useIsInstalledApp()).result.current).toBe(false);

        vi.stubGlobal('matchMedia', undefined);
        expect(renderHook(() => useIsInstalledApp()).result.current).toBe(false);
    });

    it('asks for standalone display and follows it', () => {
        const media = stubMatchMedia(true);

        const { result } = renderHook(() => useIsInstalledApp());

        expect(media.matchMedia).toHaveBeenCalledWith(INSTALLED_APP_MEDIA_QUERY);
        expect(result.current).toBe(true);

        act(() => media.change(false));

        expect(result.current).toBe(false);
    });

    it('counts an iOS home-screen app, which reports navigator.standalone', () => {
        stubMatchMedia(false);
        Object.defineProperty(navigator, 'standalone', { value: true, configurable: true });

        expect(renderHook(() => useIsInstalledApp()).result.current).toBe(true);
    });

    it('counts Vora Desktop, whose window has the desktop bridge', () => {
        stubMatchMedia(false);
        window.voraDesktop = {} as NonNullable<typeof window.voraDesktop>;

        expect(renderHook(() => useIsInstalledApp()).result.current).toBe(true);
    });
});
