import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebVideoEngine } from './WebVideoEngine';
import type { EngineEvent, EngineSource } from './PlaybackEngine';

const hls = vi.hoisted(() => {
    interface FakeErrorData {
        fatal: boolean;
        type: string;
        details: string;
        response?: { code: number };
    }
    type FakeHandler = (event: string, data: FakeErrorData) => void;

    class FakeHls {
        static instances: FakeHls[] = [];
        static Events = { MANIFEST_PARSED: 'hlsManifestParsed', ERROR: 'hlsError' };
        static ErrorTypes = { NETWORK_ERROR: 'networkError', MEDIA_ERROR: 'mediaError', OTHER_ERROR: 'otherError' };
        static isSupported() { return true; }

        readonly handlers = new Map<string, FakeHandler>();
        readonly loadSource = vi.fn();
        readonly attachMedia = vi.fn();
        readonly startLoad = vi.fn();
        readonly stopLoad = vi.fn();
        readonly destroy = vi.fn();
        readonly recoverMediaError = vi.fn();
        bandwidthEstimate = 0;

        constructor() {
            FakeHls.instances.push(this);
        }

        on(event: string, handler: FakeHandler) {
            this.handlers.set(event, handler);
        }

        fail(data: FakeErrorData) {
            this.handlers.get(FakeHls.Events.ERROR)?.(FakeHls.Events.ERROR, data);
        }
    }

    return { FakeHls };
});

vi.mock('../../../utils/loadHls', () => ({ loadHls: () => Promise.resolve(hls.FakeHls) }));

const source = (overrides: Partial<EngineSource> = {}): EngineSource => ({
    url: '/api/streaming/play/session-1?t=signed',
    strategy: 'DirectPlay',
    container: 'mp4',
    startPosition: 0,
    title: 'Pilot',
    audioTrack: null,
    subtitleTrack: null,
    isFinished: () => false,
    ...overrides,
});

beforeEach(() => {
    hls.FakeHls.instances = [];
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve());
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('the web video engine', () => {
    it('plays the original file straight from the <video> element', async () => {
        const video = document.createElement('video');
        const engine = new WebVideoEngine({ current: video });

        await engine.load(source());

        expect(video.src).toBe(`${window.location.origin}/api/streaming/play/session-1?t=signed`);
        expect(HTMLMediaElement.prototype.play).toHaveBeenCalled();
        expect(engine.read().engine).toBe('The browser, from the file');
    });

    it('lets go of the stream when unloaded', async () => {
        const video = document.createElement('video');
        const engine = new WebVideoEngine({ current: video });
        await engine.load(source());

        engine.unload();

        expect(video.hasAttribute('src')).toBe(false);
        expect(HTMLMediaElement.prototype.load).toHaveBeenCalled();
        expect(engine.read().engine).toBe('');
    });

    it('never switches tracks inside the file, so the session restarts', () => {
        const engine = new WebVideoEngine({ current: document.createElement('video') });

        expect(engine.selectLocalTracks()).toBe(false);
    });

    it('does nothing without a <video> element', async () => {
        const engine = new WebVideoEngine({ current: null });

        await engine.load(source());

        expect(engine.canShowTextSubtitles()).toBe(false);
        expect(HTMLMediaElement.prototype.play).not.toHaveBeenCalled();
    });
});

const transcode = (overrides: Partial<EngineSource> = {}) => source({
    url: '/api/streaming/hls/s/token/movie.m3u8',
    strategy: 'Transcode',
    container: 'hls',
    ...overrides,
});

async function watch(load: Partial<EngineSource>, hlsSource: boolean) {
    const video = document.createElement('video');
    const engine = new WebVideoEngine({ current: video });
    const events: EngineEvent[] = [];
    engine.on(event => events.push(event));
    await engine.load(hlsSource ? transcode(load) : source(load));
    const instance = hls.FakeHls.instances.at(-1);
    return { video, engine, events, instance };
}

const notFound = { fatal: true, type: 'networkError', details: 'fragLoadError', response: { code: 404 } };

describe('a stream that fails', () => {
    it('reports a 404 from hls.js after the picture has played, once, and stops asking', async () => {
        const { video, events, instance } = await watch({}, true);
        video.dispatchEvent(new Event('playing'));

        instance?.fail(notFound);
        instance?.fail(notFound);

        expect(events).toEqual([{
            type: 'error',
            error: { message: 'The server answered 404 when the player asked for more of the video.', beforeFirstFrame: false },
        }]);
        expect(instance?.stopLoad).toHaveBeenCalled();
        expect(instance?.startLoad).not.toHaveBeenCalled();
    });

    it('says when the stream failed before its first frame', async () => {
        const { events, instance } = await watch({}, true);

        instance?.fail(notFound);

        expect(events).toEqual([expect.objectContaining({ type: 'error', error: expect.objectContaining({ beforeFirstFrame: true }) })]);
    });

    it('still ends the video on a 410', async () => {
        const { events, instance } = await watch({}, true);

        instance?.fail({ fatal: false, type: 'networkError', details: 'fragLoadError', response: { code: 410 } });

        expect(events).toEqual([{ type: 'ended' }]);
        expect(instance?.stopLoad).toHaveBeenCalled();
    });

    it('asks once more when the server gave no answer, then reports it', async () => {
        const { events, instance } = await watch({}, true);
        const noAnswer = { fatal: true, type: 'networkError', details: 'fragLoadTimeOut' };

        instance?.fail(noAnswer);
        expect(instance?.startLoad).toHaveBeenCalledOnce();
        expect(events).toEqual([]);

        instance?.fail(noAnswer);
        expect(instance?.startLoad).toHaveBeenCalledOnce();
        expect(events).toEqual([expect.objectContaining({
            type: 'error',
            error: expect.objectContaining({ message: "The server didn't answer when the player asked for more of the video." }),
        })]);
    });

    it('reports an error hls.js gives up on instead of freezing', async () => {
        const { events, instance } = await watch({}, true);

        instance?.fail({ fatal: true, type: 'mediaError', details: 'bufferAppendError' });
        expect(instance?.recoverMediaError).toHaveBeenCalledOnce();
        expect(events).toEqual([]);

        instance?.fail({ fatal: true, type: 'mediaError', details: 'bufferAppendError' });
        expect(instance?.destroy).toHaveBeenCalled();
        expect(events).toEqual([expect.objectContaining({ type: 'error', error: expect.objectContaining({ message: "The browser couldn't play the video." }) })]);
    });

    it('ignores a failure after the video has finished', async () => {
        const { events, instance } = await watch({ isFinished: () => true }, true);

        instance?.fail(notFound);

        expect(events).toEqual([]);
    });

    it('reports the <video> element failing on a file it plays itself', async () => {
        const { video, events } = await watch({}, false);
        video.dispatchEvent(new Event('playing'));

        Object.defineProperty(video, 'error', { value: { code: 2 }, configurable: true });
        video.dispatchEvent(new Event('error'));

        expect(events).toEqual([{
            type: 'error',
            error: { message: 'The connection to the server was lost while loading the video.', beforeFirstFrame: false },
        }]);
    });

    it('leaves <video> element errors to hls.js while hls.js plays the stream', async () => {
        const { video, events } = await watch({}, true);

        video.dispatchEvent(new Event('error'));

        expect(events).toEqual([]);
    });

    it('stops listening once the stream is unloaded', async () => {
        const { video, engine, events } = await watch({}, false);

        engine.unload();
        video.dispatchEvent(new Event('error'));

        expect(events).toEqual([]);
    });
});
