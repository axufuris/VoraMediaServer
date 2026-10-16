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

class FakeNode {
    connected: FakeNode[] = [];
    connect(target: FakeNode) { this.connected.push(target); return target; }
    disconnect() { this.connected = []; }
}

class FakeSource extends FakeNode {
    constructor(readonly mediaElement: HTMLMediaElement) { super(); }
}

class FakeParam {
    value = 1;
    cancelScheduledValues() { }
    setValueAtTime() { }
    linearRampToValueAtTime() { }
}

class FakeGain extends FakeNode {
    gain = new FakeParam();
}

class FakeAnalyser extends FakeNode {
    fftSize = 0;
    smoothingTimeConstant = 0;
}

const created = vi.hoisted(() => ({ contexts: 0, sources: [] as HTMLMediaElement[] }));

class FakeAudioContext {
    state = 'running';
    currentTime = 0;
    destination = new FakeNode();
    constructor() { created.contexts++; }
    createGain() { return new FakeGain(); }
    createAnalyser() { return new FakeAnalyser(); }
    createBiquadFilter() { return Object.assign(new FakeNode(), { type: '', frequency: new FakeParam(), gain: new FakeParam(), Q: new FakeParam() }); }
    createMediaElementSource(element: HTMLMediaElement) {
        if (created.sources.includes(element)) throw new Error('InvalidStateError: already connected');
        created.sources.push(element);
        return new FakeSource(element);
    }
    resume() { return Promise.resolve(); }
}

const wrapper = ({ children }: { children: ReactNode }) => <PlayerProvider>{children}</PlayerProvider>;

const track = (id: string): PlayableMedia => ({ id, title: `Track ${id}`, streamUrl: '', playbackContextType: 'Music' });

beforeEach(() => {
    created.contexts = 0;
    created.sources = [];
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve());
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
});

afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
});

describe('the music visualizer', () => {
    it('follows the player onto a new video element instead of listening to the old one', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        const first = document.createElement('video');
        const second = document.createElement('video');
        result.current.videoRef.current = first;

        act(() => result.current.playQueue([track('1'), track('2')], 0));
        await waitFor(() => expect(created.sources).toEqual([first]));
        const analyser = result.current.getAudioAnalyser();

        result.current.videoRef.current = second;
        act(() => result.current.nextTrack());

        await waitFor(() => expect(created.sources).toEqual([first, second]));
        expect(created.contexts).toBe(1);
        expect(result.current.getAudioAnalyser()).toBe(analyser);
    });

    it('does not connect the same element twice', async () => {
        const { result } = renderHook(() => usePlayer(), { wrapper });
        result.current.videoRef.current = document.createElement('video');

        act(() => result.current.playQueue([track('1'), track('2')], 0));
        await waitFor(() => expect(created.sources).toHaveLength(1));

        act(() => result.current.nextTrack());

        await waitFor(() => expect(result.current.currentMedia?.id).toBe('2'));
        expect(created.sources).toHaveLength(1);
    });
});
