import { describe, expect, it, vi } from 'vitest';
import { fakeDesktopBridge } from '../../../test/fakeDesktopBridge';
import { DesktopMpvEngine } from './DesktopMpvEngine';
import type { EngineEvent, EngineSource } from './PlaybackEngine';
import { streamingService } from '../../../api/Streaming/streamingService';

vi.mock('../../../api/Streaming/streamingService', () => ({
    streamingService: { fetchSubtitleText: vi.fn(() => Promise.resolve('WEBVTT\n\n00:00.000 --> 00:01.000\nHello')) },
}));

const source = (overrides: Partial<EngineSource> = {}): EngineSource => ({
    url: '/api/streaming/play/session-1?t=signed',
    strategy: 'DirectPlay',
    startPosition: 754,
    title: 'Pilot',
    audioTrack: { position: 1, codec: 'truehd', language: 'eng' },
    subtitleTrack: { position: 0, codec: 'hdmv_pgs_subtitle', language: 'eng' },
    isFinished: () => false,
    ...overrides,
});

function engineWithEvents() {
    const fake = fakeDesktopBridge();
    const engine = new DesktopMpvEngine(fake.bridge);
    const events: EngineEvent[] = [];
    engine.on(event => events.push(event));
    return { fake, engine, events };
}

describe('the desktop mpv engine', () => {
    it('opens mpv at the resume position with the tracks the server chose', async () => {
        const { fake, engine } = engineWithEvents();

        await engine.load(source());

        expect(fake.bridge.player.open).toHaveBeenCalledWith({
            url: `${window.location.origin}/api/streaming/play/session-1?t=signed`,
            startSeconds: 754,
            title: 'Pilot',
            audioTrack: { position: 1, codec: 'truehd', language: 'eng' },
            subtitleTrack: { position: 0, codec: 'hdmv_pgs_subtitle', language: 'eng' },
        });
    });

    it('leaves track choice to the stream when the server converts it', async () => {
        const { fake, engine } = engineWithEvents();

        await engine.load(source({ strategy: 'Transcode', url: '/api/streaming/hls/s/token/session-1.m3u8' }));

        expect(fake.bridge.player.open).toHaveBeenCalledWith(expect.objectContaining({ audioTrack: null, subtitleTrack: null }));
    });

    it('loads the next item into the open mpv instead of starting another', async () => {
        const { fake, engine } = engineWithEvents();
        await engine.load(source());

        engine.unload();
        await engine.load(source({ url: '/api/streaming/play/session-2?t=signed', startPosition: 0 }));

        expect(fake.bridge.player.open).toHaveBeenCalledTimes(1);
        expect(fake.bridge.player.command).toHaveBeenCalledWith({ type: 'load-next', request: expect.objectContaining({ startSeconds: 0 }) });
    });

    it('switches tracks in the file only while it plays the original', async () => {
        const { fake, engine } = engineWithEvents();
        await engine.load(source({ strategy: 'Remux' }));
        expect(engine.selectLocalTracks({ audioTrack: { position: 0, codec: 'aac', language: 'jpn' } })).toBe(false);

        await engine.load(source());
        expect(engine.selectLocalTracks({ subtitleTrack: null })).toBe(true);
        expect(fake.bridge.player.command).toHaveBeenCalledWith({ type: 'tracks', subtitleTrack: null });
    });

    it('adds a sidecar subtitle as text, so no token reaches mpv', async () => {
        const { fake, engine } = engineWithEvents();
        await engine.load(source());

        const result = await engine.showTextSubtitle({ sessionId: 'session-1', trackId: 'sub-srt', title: 'English', track: null }, () => true);

        expect(result).toBe('shown');
        expect(streamingService.fetchSubtitleText).toHaveBeenCalledWith('session-1', 'sub-srt', undefined);
        expect(fake.bridge.player.command).toHaveBeenCalledWith({ type: 'sub-add', subtitle: { title: 'English', vtt: expect.stringContaining('Hello') } });
    });

    it('reports where it is from the time mpv sends', async () => {
        const { fake, engine, events } = engineWithEvents();
        await engine.load(source());

        fake.emit({ type: 'time', position: 800, duration: 2700 });
        fake.emit({ type: 'paused', paused: true });

        expect(engine.snapshot()).toEqual({ position: 800, duration: 2700, paused: true });
        expect(events).toContainEqual({ type: 'time', position: 800, duration: 2700 });
    });

    it('says whether an error came before the first frame', async () => {
        const { fake, engine, events } = engineWithEvents();
        await engine.load(source());

        fake.emit({ type: 'error', message: 'Failed to open the stream' });
        fake.emit({ type: 'loaded' });
        fake.emit({ type: 'error', message: 'Connection lost' });

        expect(events.filter(event => event.type === 'error')).toEqual([
            { type: 'error', error: { message: 'Failed to open the stream', beforeFirstFrame: true } },
            { type: 'error', error: { message: 'Connection lost', beforeFirstFrame: false } },
        ]);
    });

    it('treats the viewer closing the mpv window as leaving the player', async () => {
        const { fake, engine, events } = engineWithEvents();
        await engine.load(source());

        fake.emit({ type: 'closed' });

        expect(events).toContainEqual({ type: 'intent', intent: { type: 'exit' } });
    });

    it('does not report an exit after it was told to stop, or after mpv failed', async () => {
        const { fake, engine, events } = engineWithEvents();
        await engine.load(source());
        await engine.stop();
        fake.emit({ type: 'closed' });

        await engine.load(source());
        fake.emit({ type: 'error', message: 'mpv crashed' });
        fake.emit({ type: 'closed' });

        expect(fake.bridge.player.close).toHaveBeenCalledTimes(1);
        expect(events.some(event => event.type === 'intent')).toBe(false);
    });

    it('refuses a stream address mpv could not open', async () => {
        const { fake, engine, events } = engineWithEvents();

        await engine.load(source({ url: 'blob:https://vora.example.com/1234' }));

        expect(fake.bridge.player.open).not.toHaveBeenCalled();
        expect(events).toContainEqual({ type: 'error', error: { message: 'The server sent a stream address the Vora player cannot open.', beforeFirstFrame: true } });
    });
});
