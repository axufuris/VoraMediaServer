import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { EngineEventHub, type PlaybackEngine } from './engine/PlaybackEngine';
import { useStreamRecovery, type RecoverableStream } from './useStreamRecovery';

const alert = vi.fn(() => Promise.resolve());

vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert, confirm: () => Promise.resolve(false), prompt: () => Promise.resolve(null) }),
}));

const NotFound = 'The server answered 404 when the player asked for more of the video.';

function fakeEngine() {
    const events = new EngineEventHub();
    const engine: PlaybackEngine = {
        kind: 'web',
        load: () => Promise.resolve(),
        unload: () => { },
        selectLocalTracks: () => false,
        canShowTextSubtitles: () => false,
        showTextSubtitle: () => Promise.resolve('unavailable'),
        hideTextSubtitle: () => { },
        setChromeState: () => { },
        setScrubSprite: () => { },
        read: () => ({ engine: '', droppedFrames: null, totalFrames: null, bufferedSeconds: null, bandwidthKbps: null }),
        on: handler => events.add(handler),
    };
    const fail = (beforeFirstFrame: boolean) => act(() => {
        events.emit({ type: 'error', error: { message: NotFound, beforeFirstFrame } });
    });
    return { engine, fail };
}

function playAt(video: HTMLVideoElement, seconds: number, paused: boolean) {
    Object.defineProperty(video, 'currentTime', { value: seconds, configurable: true, writable: true });
    Object.defineProperty(video, 'paused', { value: paused, configurable: true });
    act(() => { video.dispatchEvent(new Event('timeupdate')); });
}

function setUp(stream: RecoverableStream = { key: '/api/streaming/play/one', startPosition: 0 }) {
    const video = document.createElement('video');
    const { engine, fail } = fakeEngine();
    const restartStream = vi.fn<(position: number) => Promise<void>>(() => Promise.resolve());
    const closePlayer = vi.fn();
    const hook = renderHook((current: RecoverableStream) => useStreamRecovery({
        engine,
        videoRef: { current: video },
        stream: current,
        restartStream,
        closePlayer,
    }), { initialProps: stream });
    return { video, fail, restartStream, closePlayer, hook };
}

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-22T20:00:00Z'));
    alert.mockClear();
});

afterEach(() => {
    vi.useRealTimers();
});

describe('recovering a stream that dies after playing', () => {
    it('asks for the stream again at the position it had played to', async () => {
        const { video, fail, restartStream, closePlayer } = setUp();
        playAt(video, 1903.5, false);

        await fail(false);

        expect(restartStream).toHaveBeenCalledWith(1903.5);
        expect(alert).not.toHaveBeenCalled();
        expect(closePlayer).not.toHaveBeenCalled();
    });

    it('waits for the viewer to press play when the video was paused', async () => {
        const { video, fail, restartStream } = setUp();
        playAt(video, 1903.5, true);

        await fail(false);
        expect(restartStream).not.toHaveBeenCalled();

        act(() => { video.dispatchEvent(new Event('play')); });
        expect(restartStream).toHaveBeenCalledOnce();
        expect(restartStream).toHaveBeenCalledWith(1903.5);
    });

    it('keeps the place it played to when the failed video reports 0', async () => {
        const { video, fail, restartStream } = setUp();
        playAt(video, 1903.5, true);

        await fail(false);
        playAt(video, 0, true);
        act(() => { video.dispatchEvent(new Event('play')); });

        expect(restartStream).toHaveBeenCalledWith(1903.5);
    });

    it('shows the error and closes the player for a stream that never played', async () => {
        const { fail, restartStream, closePlayer } = setUp();

        await fail(true);

        await waitFor(() => expect(closePlayer).toHaveBeenCalled());
        expect(alert).toHaveBeenCalledWith(expect.objectContaining({ title: "This video couldn't play", message: NotFound }));
        expect(restartStream).not.toHaveBeenCalled();
    });

    it('asks again at most once a minute, and shows the error the second time inside it', async () => {
        const { video, fail, restartStream, closePlayer, hook } = setUp();
        playAt(video, 600, false);
        await fail(false);
        expect(restartStream).toHaveBeenCalledOnce();

        hook.rerender({ key: '/api/streaming/play/two', startPosition: 600 });
        vi.setSystemTime(new Date('2026-10-22T20:00:30Z'));
        await fail(false);

        await waitFor(() => expect(closePlayer).toHaveBeenCalled());
        expect(alert).toHaveBeenCalledWith(expect.objectContaining({ title: 'This video stopped playing' }));
        expect(restartStream).toHaveBeenCalledOnce();
    });

    it('asks again once a minute has passed since the last time', async () => {
        const { video, fail, restartStream, hook } = setUp();
        playAt(video, 600, false);
        await fail(false);

        hook.rerender({ key: '/api/streaming/play/two', startPosition: 600 });
        vi.setSystemTime(new Date('2026-10-22T20:01:01Z'));
        playAt(video, 640, false);
        await fail(false);

        expect(restartStream).toHaveBeenCalledTimes(2);
        expect(restartStream).toHaveBeenLastCalledWith(640);
    });

    it('forgets a waiting restart when another stream starts', async () => {
        const { video, fail, restartStream, hook } = setUp();
        playAt(video, 1903.5, true);
        await fail(false);

        hook.rerender({ key: '/api/streaming/play/next-episode', startPosition: 0 });
        act(() => { video.dispatchEvent(new Event('play')); });

        expect(restartStream).not.toHaveBeenCalled();
    });
});
