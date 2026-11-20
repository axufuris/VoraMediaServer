import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlayerProvider } from './PlayerContext';
import { usePlayer, type PlayableMedia } from './usePlayer';

const saveResume = vi.hoisted(() => vi.fn());
const resolved = () => Promise.resolve(undefined);

vi.mock('../api/Music/musicService', () => ({
    musicService: new Proxy({}, {
        get: (_target, prop) => prop === 'saveResume'
            ? saveResume
            : prop === 'resolveTrackStream'
                ? () => Promise.resolve({ url: 'https://example.test/track.mp3', quality: null })
                : resolved,
    }),
}));
vi.mock('../api/Streaming/streamingService', () => ({ streamingService: new Proxy({}, { get: () => resolved }) }));
vi.mock('../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
vi.mock('../dialogs', () => ({
    useDialog: () => ({ alert: resolved, confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const wrapper = ({ children }: { children: ReactNode }) => <PlayerProvider>{children}</PlayerProvider>;

const song = (id: string): PlayableMedia => ({
    id,
    title: `Track ${id}`,
    subtitle: 'Artist — Album',
    streamUrl: '',
    playbackContextType: 'Music',
    musicSource: { type: 'Playlist', id: 'p1' },
});

describe('the player saving where a music queue stopped', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        saveResume.mockReset().mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('saves a queue it starts, with the source it came from', () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });

        act(() => result.current.playQueue([song('1'), song('2'), song('3')], 1));
        act(() => { vi.advanceTimersByTime(1100); });

        expect(saveResume).toHaveBeenCalledWith({ source: 'Playlist', sourceId: 'p1', trackIds: ['1', '2', '3'], index: 1 }, undefined);
    });

    it('keeps the point when the player is closed', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        act(() => result.current.playQueue([song('1'), song('2')], 0));
        act(() => { vi.advanceTimersByTime(1100); });
        saveResume.mockClear();

        await act(async () => { await result.current.closePlayer(); });

        expect(saveResume).not.toHaveBeenCalledWith(expect.objectContaining({ trackIds: [] }), expect.anything());
    });

    it('clears the point when the last song ends by itself', () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        const element = document.createElement('video');
        result.current.videoRef.current = element;
        act(() => result.current.playQueue([song('1')], 0));
        act(() => { vi.advanceTimersByTime(1100); });
        saveResume.mockClear();

        act(() => { element.dispatchEvent(new Event('ended')); });
        act(() => { vi.advanceTimersByTime(1100); });

        expect(saveResume).toHaveBeenCalledTimes(1);
        expect(saveResume).toHaveBeenCalledWith({ source: 'Playlist', sourceId: 'p1', trackIds: [], index: 0 }, undefined);
    });

    it('does not clear the point when a song ends and another follows', () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        const element = document.createElement('video');
        result.current.videoRef.current = element;
        act(() => result.current.playQueue([song('1'), song('2')], 0));
        act(() => { vi.advanceTimersByTime(1100); });
        saveResume.mockClear();

        act(() => { element.dispatchEvent(new Event('ended')); });
        act(() => { vi.advanceTimersByTime(1100); });

        expect(saveResume).not.toHaveBeenCalledWith(expect.objectContaining({ trackIds: [] }), expect.anything());
        expect(saveResume).toHaveBeenCalledWith(expect.objectContaining({ index: 1 }), undefined);
    });
});
