import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, waitFor, cleanup, renderHook, act } from '@testing-library/react';

// vi.mock factory hoists above local consts; declare mocks via vi.hoisted.
const mocks = vi.hoisted(() => ({
    getHistoryMock: vi.fn(),
    useSignalREventMock: vi.fn(),
}));

vi.mock('../api/YouTube/youtubeService', () => ({
    youtubeService: {
        getHistory: mocks.getHistoryMock,
    },
}));

vi.mock('./useSignalREvent', () => ({
    useSignalREvent: mocks.useSignalREventMock,
}));

describe('useYouTubeWatchedSet', () => {
    beforeEach(() => {
        mocks.getHistoryMock.mockReset();
        mocks.useSignalREventMock.mockReset();
    });

    afterEach(() => {
        cleanup();
    });

    it('loads history on mount and exposes has() for known video ids', async () => {
        mocks.getHistoryMock.mockResolvedValue([
            { videoId: 'abc' },
            { videoId: 'xyz' },
        ]);

        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');
        const { result } = renderHook(() => useYouTubeWatchedSet('server-1'));

        await waitFor(() => {
            expect(result.current.has('abc')).toBe(true);
        });

        expect(result.current.has('xyz')).toBe(true);
        expect(result.current.has('unknown')).toBe(false);
        expect(mocks.getHistoryMock).toHaveBeenCalledWith('server-1');
    });

    it('falls back to empty set on fetch error', async () => {
        mocks.getHistoryMock.mockRejectedValue(new Error('boom'));

        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');
        const { result } = renderHook(() => useYouTubeWatchedSet('server-1'));

        await waitFor(() => {
            // refresh promise resolved (errored and swallowed)
            expect(mocks.getHistoryMock).toHaveBeenCalled();
        });

        expect(result.current.has('anything')).toBe(false);
    });

    it('subscribes to YouTubeAccessChanged signal event', async () => {
        mocks.getHistoryMock.mockResolvedValue([]);

        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');
        renderHook(() => useYouTubeWatchedSet('server-1'));

        expect(mocks.useSignalREventMock).toHaveBeenCalledWith(
            'YouTubeAccessChanged',
            expect.any(Function)
        );
    });

    it('refresh() reloads the history and updates the set', async () => {
        mocks.getHistoryMock.mockResolvedValueOnce([{ videoId: 'first' }]);
        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');
        const { result } = renderHook(() => useYouTubeWatchedSet('server-1'));

        await waitFor(() => {
            expect(result.current.has('first')).toBe(true);
        });

        // Set up a different history for the refresh call
        mocks.getHistoryMock.mockResolvedValueOnce([
            { videoId: 'first' },
            { videoId: 'second' },
        ]);

        await act(async () => {
            await result.current.refresh();
        });

        expect(result.current.has('first')).toBe(true);
        expect(result.current.has('second')).toBe(true);
    });

    it('signal handler invokes refresh', async () => {
        mocks.getHistoryMock.mockResolvedValueOnce([]);
        let signalHandler: (() => void) | null = null;
        mocks.useSignalREventMock.mockImplementation((_evt: string, cb: () => void) => {
            signalHandler = cb;
        });

        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');
        const { result } = renderHook(() => useYouTubeWatchedSet('server-1'));

        // Initial fetch
        await waitFor(() => {
            expect(mocks.getHistoryMock).toHaveBeenCalledTimes(1);
        });

        // Configure next call's result, then fire the signal handler
        mocks.getHistoryMock.mockResolvedValueOnce([{ videoId: 'after-signal' }]);
        await act(async () => {
            signalHandler?.();
        });

        await waitFor(() => {
            expect(result.current.has('after-signal')).toBe(true);
        });
    });

    it('refetches when serverId changes', async () => {
        mocks.getHistoryMock.mockResolvedValue([]);
        const { useYouTubeWatchedSet } = await import('./useYouTubeWatchedSet');

        const { rerender } = renderHook(
            ({ id }) => useYouTubeWatchedSet(id),
            { initialProps: { id: 'server-1' } }
        );

        await waitFor(() => {
            expect(mocks.getHistoryMock).toHaveBeenCalledWith('server-1');
        });

        rerender({ id: 'server-2' });

        await waitFor(() => {
            expect(mocks.getHistoryMock).toHaveBeenCalledWith('server-2');
        });
    });
});
