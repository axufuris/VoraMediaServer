import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebVideoEngine } from './WebVideoEngine';
import type { EngineSource } from './PlaybackEngine';

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
