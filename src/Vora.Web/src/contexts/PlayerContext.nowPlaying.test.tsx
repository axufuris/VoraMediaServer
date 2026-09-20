import { describe, it, expect, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlayerProvider } from './PlayerContext';
import { usePlayer, type PlayableMedia } from './usePlayer';

const resolved = () => Promise.resolve(undefined);

vi.mock('../api/Music/musicService', () => ({
    musicService: new Proxy({}, {
        get: (_target, prop) => prop === 'resolveTrackStreamUrl'
            ? () => Promise.resolve('https://example.test/track.mp3')
            : resolved,
    }),
}));

vi.mock('../api/Streaming/streamingService', () => ({
    streamingService: new Proxy({}, { get: () => resolved }),
}));

vi.mock('../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));

vi.mock('../dialogs', () => ({
    useDialog: () => ({ alert: resolved, confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const wrapper = ({ children }: { children: ReactNode }) => <PlayerProvider>{children}</PlayerProvider>;

const track = (id: string): PlayableMedia => ({
    id,
    title: `Track ${id}`,
    subtitle: 'blink-182 — Cheshire Cat',
    streamUrl: '',
    playbackContextType: 'Music',
});

const album = [track('1'), track('2'), track('3')];

describe('starting music', () => {
    // Clicking a song left the player collapsed to the mini bar: playMedia
    // expanded it, then an effect on the track change minimized it again.
    it('opens the now-playing screen', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });

        act(() => result.current.playQueue(album, 0));

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));
        expect(result.current.isFullscreen).toBe(true);
    });

    // The bar underneath is the only inline music player; the older expanded
    // radio layout must never be what music lands in.
    it('keeps the inline player as the bar', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });

        act(() => result.current.playQueue(album, 0));

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));
        expect(result.current.isMinimized).toBe(true);
    });
});

describe('changing tracks', () => {
    it('stays on the now-playing screen when skipping ahead', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue(album, 0));
        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));

        act(() => result.current.nextTrack());

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('2'));
        expect(result.current.isFullscreen).toBe(true);
    });

    it('advances to the next track in the queue', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue(album, 0));
        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));

        act(() => result.current.nextTrack());

        await waitFor(() => expect(result.current.queueIndex).toBe(1));
        expect(result.current.currentMedia?.id).toBe('2');
    });

    // Skipping from the mini bar must not throw the screen open over whatever
    // the viewer was browsing.
    it('does not reopen the screen after it was minimized', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue(album, 0));
        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));

        act(() => result.current.setFullscreen(false));
        act(() => result.current.nextTrack());

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('2'));
        expect(result.current.isFullscreen).toBe(false);
        expect(result.current.isMinimized).toBe(true);
    });

    it('does not reopen the screen when jumping within the queue', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue(album, 0));
        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));

        act(() => result.current.setFullscreen(false));
        act(() => result.current.jumpToQueueIndex(2));

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('3'));
        expect(result.current.isFullscreen).toBe(false);
    });
});

describe('other media', () => {
    it('does not open the music screen for a radio station', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });

        act(() => result.current.playMedia({ id: 'station', title: 'KEXP', streamUrl: 'https://example.test/live', playbackContextType: 'LiveRadio' }));

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('station'));
        expect(result.current.isFullscreen).toBe(false);
        expect(result.current.isMinimized).toBe(false);
    });

    it('closes the music screen when something else starts', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue(album, 0));
        await waitFor(() => expect(result.current.isFullscreen).toBe(true));

        act(() => result.current.playMedia({ id: 'station', title: 'KEXP', streamUrl: 'https://example.test/live', playbackContextType: 'LiveRadio' }));

        await waitFor(() => expect(result.current.isFullscreen).toBe(false));
    });
});
