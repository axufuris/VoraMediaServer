import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, cleanup, act } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
    useSignalREventMock: vi.fn(),
    getActiveServerMock: vi.fn(),
    getServerMock: vi.fn(),
}));

vi.mock('./useSignalREvent', () => ({
    useSignalREvent: mocks.useSignalREventMock,
}));

vi.mock('../utils/serverVault', () => ({
    serverVault: {
        getActiveServer: mocks.getActiveServerMock,
        getServer: mocks.getServerMock,
    },
}));

const VTT_SAMPLE = `WEBVTT

00:00:00.000 --> 00:00:05.000
sprite.jpg#xywh=0,0,160,90

00:00:05.000 --> 00:00:10.000
sprite.jpg#xywh=160,0,160,90

00:00:10.000 --> 00:00:15.000
sprite.jpg#xywh=320,0,160,90
`;

describe('useVideoThumbnails', () => {
    let originalFetch: typeof fetch;
    let createObjectURLOriginal: typeof URL.createObjectURL;
    let revokeObjectURLOriginal: typeof URL.revokeObjectURL;

    beforeEach(() => {
        mocks.useSignalREventMock.mockReset();
        mocks.getActiveServerMock.mockReset();
        mocks.getServerMock.mockReset();
        mocks.getActiveServerMock.mockReturnValue({ url: 'http://server-1', token: 'token-1' });

        originalFetch = global.fetch;
        createObjectURLOriginal = URL.createObjectURL;
        revokeObjectURLOriginal = URL.revokeObjectURL;
        let urlCounter = 0;
        URL.createObjectURL = vi.fn(() => `blob:fake-${++urlCounter}`);
        URL.revokeObjectURL = vi.fn();
    });

    afterEach(() => {
        cleanup();
        global.fetch = originalFetch;
        URL.createObjectURL = createObjectURLOriginal;
        URL.revokeObjectURL = revokeObjectURLOriginal;
    });

    it('returns unavailable when no mediaItemId provided', async () => {
        const { useVideoThumbnails } = await import('./useVideoThumbnails');

        const { result } = renderHook(() => useVideoThumbnails(undefined));

        await waitFor(() => expect(result.current.available).toBe(false));
        expect(result.current.findCue(5)).toBeNull();
    });

    it('loads VTT and sprite, then findCue returns matching cue', async () => {
        const fetchMock = vi.fn().mockImplementation((url: string) => {
            if (url.includes('.vtt')) {
                return Promise.resolve({ ok: true, text: () => Promise.resolve(VTT_SAMPLE) });
            }
            if (url.includes('.jpg')) {
                return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob(['fake'])) });
            }
            return Promise.reject(new Error('unexpected url ' + url));
        });
        global.fetch = fetchMock as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        const { result } = renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(result.current.available).toBe(true));

        expect(result.current.width).toBe(160);
        expect(result.current.height).toBe(90);
        expect(result.current.spriteUrl).toMatch(/^blob:/);

        const cueAtZero = result.current.findCue(0);
        expect(cueAtZero).not.toBeNull();
        expect(cueAtZero!.x).toBe(0);

        const cueAtMid = result.current.findCue(7.5);
        expect(cueAtMid).not.toBeNull();
        expect(cueAtMid!.x).toBe(160);

        const cueAtThird = result.current.findCue(12);
        expect(cueAtThird).not.toBeNull();
        expect(cueAtThird!.x).toBe(320);
    });

    it('findCue snaps past the final cue to the last cue', async () => {
        global.fetch = vi.fn().mockImplementation((url: string) => {
            if (url.includes('.vtt')) return Promise.resolve({ ok: true, text: () => Promise.resolve(VTT_SAMPLE) });
            return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob(['x'])) });
        }) as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        const { result } = renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(result.current.available).toBe(true));

        // 999 is past last cue's end (15s); the binary search clamps to the last cue.
        const cue = result.current.findCue(999);
        expect(cue).not.toBeNull();
        expect(cue!.x).toBe(320);
    });

    it('sets available=false when VTT fetch fails', async () => {
        global.fetch = vi.fn().mockResolvedValue({ ok: false }) as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        const { result } = renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(global.fetch).toHaveBeenCalled());
        expect(result.current.available).toBe(false);
    });

    it('sets available=false when VTT parses to zero cues', async () => {
        global.fetch = vi.fn().mockImplementation((url: string) => {
            if (url.includes('.vtt')) return Promise.resolve({ ok: true, text: () => Promise.resolve('WEBVTT\n\n') });
            return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob()) });
        }) as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        const { result } = renderHook(() => useVideoThumbnails('media-1'));

        // Initial load resolves with an empty VTT — hook stays at available=false
        await waitFor(() => expect(global.fetch).toHaveBeenCalled());
        await act(async () => { await Promise.resolve(); });
        expect(result.current.available).toBe(false);
    });

    it('sends Authorization header when token available', async () => {
        const fetchMock = vi.fn().mockImplementation((url: string) => {
            if (url.includes('.vtt')) return Promise.resolve({ ok: true, text: () => Promise.resolve(VTT_SAMPLE) });
            return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob(['x'])) });
        });
        global.fetch = fetchMock as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
        const vttCall = fetchMock.mock.calls.find(c => String(c[0]).includes('.vtt'))!;
        expect(vttCall[1]?.headers?.Authorization).toBe('Bearer token-1');
    });

    it('subscribes to VideoThumbnailsReady SignalR event', async () => {
        global.fetch = vi.fn().mockImplementation((url: string) => {
            if (url.includes('.vtt')) return Promise.resolve({ ok: true, text: () => Promise.resolve(VTT_SAMPLE) });
            return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob(['x'])) });
        }) as unknown as typeof fetch;

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(mocks.useSignalREventMock).toHaveBeenCalled());
        expect(mocks.useSignalREventMock).toHaveBeenCalledWith(
            'VideoThumbnailsReady',
            expect.any(Function),
        );
    });

    it('signal handler reloads only when the updated id matches', async () => {
        let fetchCount = 0;
        global.fetch = vi.fn().mockImplementation((url: string) => {
            fetchCount++;
            if (url.includes('.vtt')) return Promise.resolve({ ok: true, text: () => Promise.resolve(VTT_SAMPLE) });
            return Promise.resolve({ ok: true, blob: () => Promise.resolve(new Blob(['x'])) });
        }) as unknown as typeof fetch;

        let capturedHandler: ((id: string) => void) | null = null;
        mocks.useSignalREventMock.mockImplementation((_evt: string, cb: (id: string) => void) => {
            capturedHandler = cb;
        });

        const { useVideoThumbnails } = await import('./useVideoThumbnails');
        renderHook(() => useVideoThumbnails('media-1'));

        await waitFor(() => expect(fetchCount).toBeGreaterThanOrEqual(2));
        const beforeSignal = fetchCount;

        // Fire signal for a different id — should NOT trigger a reload
        await act(async () => { capturedHandler?.('other-id'); });
        expect(fetchCount).toBe(beforeSignal);

        // Fire signal for matching id — should reload
        await act(async () => { capturedHandler?.('media-1'); });
        await waitFor(() => expect(fetchCount).toBeGreaterThan(beforeSignal));
    });
});
