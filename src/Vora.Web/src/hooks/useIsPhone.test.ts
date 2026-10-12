import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { PHONE_MEDIA_QUERY, useIsPhone } from './useIsPhone';

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

describe('useIsPhone', () => {
    afterEach(() => vi.unstubAllGlobals());

    it('is false where the browser has no matchMedia', () => {
        vi.stubGlobal('matchMedia', undefined);

        const { result } = renderHook(() => useIsPhone());

        expect(result.current).toBe(false);
    });

    it('asks for the phone breakpoint and follows it as the window resizes', () => {
        const media = stubMatchMedia(true);

        const { result } = renderHook(() => useIsPhone());

        expect(media.matchMedia).toHaveBeenCalledWith(PHONE_MEDIA_QUERY);
        expect(result.current).toBe(true);

        act(() => media.change(false));

        expect(result.current).toBe(false);
    });
});
