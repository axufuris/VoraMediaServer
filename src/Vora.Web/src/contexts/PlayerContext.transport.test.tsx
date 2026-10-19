import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { PlayerProvider } from './PlayerContext';
import { usePlayer, usePlayerTime, type PlayableMedia } from './usePlayer';
import { usePlaybackTransportHost } from './usePlaybackTransport';
import { EngineEventHub, type PlaybackTransport } from '../components/Player/engine/PlaybackEngine';
import { streamingService } from '../api/Streaming/streamingService';

const resolved = () => Promise.resolve(undefined);

vi.mock('../api/Music/musicService', () => ({
    musicService: new Proxy({}, { get: () => resolved }),
}));

vi.mock('../api/Streaming/streamingService', () => ({
    streamingService: {
        pingSession: vi.fn(() => Promise.resolve()),
        stopSession: vi.fn(() => Promise.resolve()),
        startSession: vi.fn(() => Promise.resolve({ sessionId: 'session-2', streamUrl: '/api/streaming/play/session-2?t=x' })),
    },
}));

vi.mock('../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));

vi.mock('../dialogs', () => ({
    useDialog: () => ({ alert: resolved, confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const wrapper = ({ children }: { children: ReactNode }) => <PlayerProvider>{children}</PlayerProvider>;

const episode: PlayableMedia = { id: 'episode', title: 'Episode', streamUrl: '/api/streaming/play/session-1?t=x', sessionId: 'session-1', strategy: 'DirectPlay' };

function fakeTransport() {
    const events = new EngineEventHub();
    const playback = { position: 42, duration: 1200, paused: false };
    const transport: PlaybackTransport = {
        play: vi.fn(),
        pause: vi.fn(),
        seek: vi.fn(),
        snapshot: () => ({ ...playback }),
        stop: vi.fn(() => Promise.resolve()),
        on: handler => events.add(handler),
    };
    return { transport, events, playback };
}

function renderPlayer() {
    return renderHook(() => ({ player: usePlayer(), time: usePlayerTime(), host: usePlaybackTransportHost() }), { wrapper });
}

beforeEach(() => {
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve());
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
    vi.useFakeTimers();
    vi.mocked(streamingService.pingSession).mockClear();
    vi.mocked(streamingService.stopSession).mockClear();
});

afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
});

describe('a player engine that is not the <video> element', () => {
    it('is pinged every ten seconds with the position it reports', () => {
        const { result } = renderPlayer();
        const { transport, playback } = fakeTransport();
        act(() => { result.current.host.attachTransport(transport); });
        act(() => result.current.player.playMedia(episode));

        act(() => { vi.advanceTimersByTime(9_999); });
        expect(streamingService.pingSession).not.toHaveBeenCalled();

        act(() => { vi.advanceTimersByTime(1); });
        expect(streamingService.pingSession).toHaveBeenLastCalledWith('session-1', 42, 1200, false, undefined);

        playback.position = 52;
        playback.paused = true;
        act(() => { vi.advanceTimersByTime(10_000); });
        expect(streamingService.pingSession).toHaveBeenCalledTimes(2);
        expect(streamingService.pingSession).toHaveBeenLastCalledWith('session-1', 52, 1200, true, undefined);
    });

    it('drives the clock and the play state the chrome reads', () => {
        const { result } = renderPlayer();
        const { transport, events } = fakeTransport();
        act(() => { result.current.host.attachTransport(transport); });
        act(() => result.current.player.playMedia(episode));

        act(() => events.emit({ type: 'time', position: 61, duration: 1300 }));
        expect(result.current.time).toEqual({ currentTime: 61, duration: 1300 });

        act(() => events.emit({ type: 'paused', paused: true }));
        expect(result.current.player.isPlaying).toBe(false);
    });

    it('takes the play, pause and seek calls', () => {
        const { result } = renderPlayer();
        const { transport } = fakeTransport();
        act(() => { result.current.host.attachTransport(transport); });
        act(() => result.current.player.playMedia(episode));

        act(() => result.current.player.togglePlayPause());
        expect(transport.pause).toHaveBeenCalled();

        act(() => result.current.player.seek(300));
        expect(transport.seek).toHaveBeenCalledWith(300);
        expect(result.current.time.currentTime).toBe(300);

        act(() => result.current.player.skipForward(30));
        expect(transport.seek).toHaveBeenLastCalledWith(72);
    });

    it('is stopped on close, and the server hears the final position', async () => {
        const { result } = renderPlayer();
        const { transport } = fakeTransport();
        act(() => { result.current.host.attachTransport(transport); });
        act(() => result.current.player.playMedia(episode));

        await act(() => result.current.player.closePlayer());

        expect(transport.stop).toHaveBeenCalled();
        expect(streamingService.pingSession).toHaveBeenCalledWith('session-1', 42, 1200, true, undefined);
        expect(streamingService.stopSession).toHaveBeenCalledWith('session-1', undefined);
    });

    it('restarts a session from the position it reports', async () => {
        const { result } = renderPlayer();
        const { transport } = fakeTransport();
        act(() => { result.current.host.attachTransport(transport); });
        act(() => result.current.player.playMedia(episode));

        await act(() => result.current.player.changeStreams('video', 'audio', 'none'));

        expect(streamingService.startSession).toHaveBeenCalledWith('episode', expect.any(String), 42, 'video', 'audio', undefined, undefined);
        expect(result.current.player.currentMedia?.startPosition).toBe(42);
    });

    it('hands the calls back to the <video> element once detached', () => {
        const { result } = renderPlayer();
        const { transport } = fakeTransport();
        let detach = () => { };
        act(() => { detach = result.current.host.attachTransport(transport); });
        act(() => detach());
        const video = document.createElement('video');
        result.current.player.videoRef.current = video;
        act(() => result.current.player.playMedia(episode));

        act(() => result.current.player.seek(12));

        expect(transport.seek).not.toHaveBeenCalled();
        expect(video.currentTime).toBe(12);
    });
});
