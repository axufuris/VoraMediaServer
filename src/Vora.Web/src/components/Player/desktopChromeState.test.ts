import { describe, expect, it } from 'vitest';
import { buildChromeState, cssColorToHex, type ChromeSource } from './desktopChromeState';

const theme = { accent: '#ff7a00', surface: '#1c1c22', text: '#f5f5f7', textMuted: '#a1a1aa' };

function controller(overrides: Partial<ChromeSource> = {}): ChromeSource {
    const base: ChromeSource = {
        currentMedia: { id: 'episode-1', title: 'Pilot', subtitle: 'S1 E1 - The Show', streamUrl: '/s', strategy: 'DirectPlay', videoTrackId: 'video-1', bandwidthKbps: 48_000 },
        mediaDetails: {
            id: 'episode-1',
            title: 'Pilot',
            type: 'Episode',
            libraryId: 'library',
            collectionIds: [],
            mediaParts: [
                { id: 'part-4k', filePath: '/a.mkv', resolution: '2160p', videoTracks: [{ id: 'video-1', codec: 'hevc', hdrType: 'HDR10' }] },
                { id: 'part-hd', filePath: '/b.mkv', resolution: '1080p', videoTracks: [{ id: 'video-2', codec: 'h264' }] },
            ],
        },
        upNextData: { nextItem: { id: 'episode-2', title: 'Second', type: 'Episode', seasonNumber: 1, episodeNumber: 2, tvShowTitle: 'The Show' }, relatedLists: [] },
        visibleMarkers: [{ type: 'Intro', startSeconds: 60, endSeconds: 150, order: 0 }],
        activeSkipPrompt: null,
        showSkipCommercial: false,
        isEnding: false,
        finished: false,
        autoPlayNext: true,
        isEpisode: true,
        playingAudioId: 'audio-en',
        effectiveSubtitleId: 'none',
        activeStreamPart: {
            id: 'part-4k',
            filePath: '/a.mkv',
            audioTracks: [{ id: 'audio-en', codec: 'truehd', language: 'eng', channels: 8 }],
            subtitleTracks: [{ id: 'sub-1', codec: 'hdmv_pgs_subtitle', language: 'eng', isForced: true }],
        },
        display: { resolution: '4K', hdr: 'HDR10', videoCodec: 'hevc', audioCodec: 'truehd', audioChannels: 8, container: 'mkv' },
    };
    return { ...base, ...overrides };
}

describe('the state the Vora player chrome draws', () => {
    it('carries the title, the markers and the tracks', () => {
        const state = buildChromeState({ controller: controller(), theme, status: { status: 'playing' }, output: null, secondsLeft: 900 });

        expect(state.title).toBe('Pilot');
        expect(state.subtitle).toBe('S1 E1 - The Show');
        expect(state.markers).toEqual([{ kind: 'Intro', start: 60, end: 150 }]);
        expect(state.audio).toEqual([{ id: 'audio-en', label: 'English', detail: 'TRUEHD 7.1', selected: true }]);
        expect(state.subtitles[0]).toMatchObject({ id: 'sub-1', selected: false, detail: expect.stringContaining('Forced') });
        expect(state.quality.options.map(option => option.selected)).toEqual([true, false]);
        expect(state.quality.currentLabel).toBe('4K · HDR10 · Direct Play');
        expect(state.canPlayNext).toBe(true);
        expect(state.canPlayPrevious).toBe(false);
    });

    it('shows the skip chip the web player would, and none while Up Next is up', () => {
        const skipping = controller({ activeSkipPrompt: { label: 'Skip Intro', target: 150, kind: 'intro' } });
        expect(buildChromeState({ controller: skipping, theme, status: { status: 'playing' }, output: null, secondsLeft: 900 }).skip).toEqual({ label: 'Skip Intro' });

        const ending = controller({ activeSkipPrompt: { label: 'Skip Credits', target: 2698, kind: 'credits-to-end' }, isEnding: true });
        const state = buildChromeState({ controller: ending, theme, status: { status: 'playing' }, output: null, secondsLeft: 11.2 });
        expect(state.skip).toBeNull();
        expect(state.upNext).toEqual({ title: 'Second', subtitle: 'S1 E2 - The Show', countdownSeconds: 12 });
    });

    it('drops the countdown when autoplay is off', () => {
        const state = buildChromeState({ controller: controller({ isEnding: true, autoPlayNext: false }), theme, status: { status: 'playing' }, output: null, secondsLeft: 5 });

        expect(state.upNext?.countdownSeconds).toBeNull();
    });

    it('explains an error and adds what mpv outputs to the Info rows', () => {
        const state = buildChromeState({
            controller: controller(),
            theme,
            status: { status: 'error', message: 'Connection lost' },
            output: { videoCodec: 'hevc', width: 3840, height: 2160, outputTransfer: 'pq', hardwareDecoder: 'd3d11va', audioCodec: 'truehd', audioChannels: 8 },
            secondsLeft: 900,
        });

        expect(state.status).toBe('error');
        expect(state.statusText).toBe('Connection lost');
        expect(state.info).toContainEqual({ label: 'Output', value: '3840×2160 · pq' });
        expect(state.info).toContainEqual({ label: 'Decoder', value: 'd3d11va' });
        expect(state.info).toContainEqual({ label: 'Bitrate', value: '48.0 Mbps' });
    });
});

describe('a template colour for the chrome', () => {
    it('is sent as hex', () => {
        expect(cssColorToHex('rgb(255, 122, 0)')).toBe('#ff7a00');
        expect(cssColorToHex('rgba(28, 28, 34, 0.9)')).toBe('#1c1c22');
        expect(cssColorToHex('#A1A1AA')).toBe('#a1a1aa');
    });

    it('is left empty when the browser could not resolve it', () => {
        expect(cssColorToHex('var(--vora-accent-500)')).toBe('');
    });
});
