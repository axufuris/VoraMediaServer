import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlayerProvider } from './PlayerContext';
import { usePlayer, type PlayableMedia } from './usePlayer';

const resolved = () => Promise.resolve(undefined);

vi.mock('../api/Music/musicService', () => ({
    musicService: new Proxy({}, {
        get: (_target, prop) => prop === 'resolveTrackStream'
            ? () => Promise.resolve({ url: 'https://example.test/track.mp3', quality: null })
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

const track = (id: string): PlayableMedia => ({ id, title: `Track ${id}`, streamUrl: '', playbackContextType: 'Music' });
const episode = (sessionId: string): PlayableMedia => ({ id: 'episode', title: 'Episode', streamUrl: 'https://example.test/e.m3u8', sessionId });

beforeEach(() => {
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve());
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('the player volume', () => {
    it('stays where it was set after the player is closed and something else is played', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        const first = document.createElement('video');
        result.current.videoRef.current = first;
        act(() => result.current.playQueue([track('1')], 0));
        await waitFor(() => expect(result.current.currentMedia?.id).toBe('1'));

        act(() => result.current.setVolume(0.1));
        expect(first.volume).toBe(0.1);

        await act(() => result.current.closePlayer());
        const next = document.createElement('video');
        result.current.videoRef.current = next;
        act(() => result.current.playQueue([track('2')], 0));

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('2'));
        expect(next.volume).toBe(0.1);
        expect(result.current.volume).toBe(0.1);
    });

    it('carries over to the element a new video session mounts', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        result.current.videoRef.current = document.createElement('video');
        act(() => result.current.playMedia(episode('a')));
        act(() => result.current.setVolume(0.4));

        const remounted = document.createElement('video');
        result.current.videoRef.current = remounted;
        act(() => result.current.playMedia(episode('b')));

        await waitFor(() => expect(remounted.volume).toBe(0.4));
    });

    it('keeps a level picked while nothing is playing and applies it to the next element', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });

        act(() => result.current.setVolume(0.3));
        expect(result.current.volume).toBe(0.3);

        const element = document.createElement('video');
        result.current.videoRef.current = element;
        act(() => result.current.playQueue([track('1')], 0));

        await waitFor(() => expect(element.volume).toBe(0.3));
    });
});
