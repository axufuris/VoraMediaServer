import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { PlayerContext, PlayerTimeContext, type PlayableMedia, type PlayerContextType } from '../../contexts/usePlayer';
import type { MediaItem, MediaMarker, UpNextItemVM } from '../../api/Media/mediaService';
import { mediaService } from '../../api/Media/mediaService';
import { profileService } from '../../api/Users/profileService';
import { streamingService } from '../../api/Streaming/streamingService';
import { EngineEventHub, type PlaybackEngine } from './engine/PlaybackEngine';
import { usePlaybackController } from './usePlaybackController';

vi.mock('../../api/Media/mediaService', () => ({
    mediaService: {
        getMarkers: vi.fn(() => Promise.resolve([])),
        getMediaItem: vi.fn(),
        getUpNext: vi.fn(() => Promise.resolve({ relatedLists: [] })),
    },
}));

vi.mock('../../api/Users/profileService', () => ({
    profileService: { getMyPlaybackPreferences: vi.fn() },
}));

vi.mock('../../api/Streaming/streamingService', () => ({
    streamingService: {
        pingSession: vi.fn(() => Promise.resolve()),
        stopSession: vi.fn(() => Promise.resolve()),
        startSession: vi.fn(),
        rememberChoice: vi.fn(() => Promise.resolve({})),
        releaseSubtitleUrls: vi.fn(),
    },
}));

vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('../../hooks/useFeatureFlags', () => ({ useFeatureFlags: () => ({ subtitleSearch: false }) }));
vi.mock('../../hooks/useVideoThumbnails', () => ({
    useVideoThumbnails: () => ({ available: false, cues: [], width: 0, height: 0, spriteUrl: '', findCue: () => null }),
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const details: MediaItem = {
    id: 'episode-1',
    title: 'Pilot',
    type: 'Episode',
    libraryId: 'library',
    collectionIds: [],
    mediaParts: [{
        id: 'part-1',
        filePath: '/tv/pilot.mkv',
        resolution: '2160p',
        videoTracks: [{ id: 'video-1', codec: 'hevc', hdrType: 'HDR10' }],
        audioTracks: [
            { id: 'audio-en', codec: 'truehd', language: 'eng', channels: 8, isDefault: true, embeddedPosition: 0 },
            { id: 'audio-ja', codec: 'eac3', language: 'jpn', channels: 6, embeddedPosition: 1 },
        ],
        subtitleTracks: [
            { id: 'sub-pgs', codec: 'hdmv_pgs_subtitle', language: 'eng', embeddedPosition: 0 },
            { id: 'sub-srt', codec: 'subrip', language: 'eng', isExternal: true, embeddedPosition: null },
        ],
    }],
};

const episode: PlayableMedia = {
    id: 'episode-1',
    title: 'Pilot',
    subtitle: 'S1 E1 - The Show',
    streamUrl: '/api/streaming/play/session-1?t=signed',
    sessionId: 'session-1',
    startPosition: 754,
    strategy: 'DirectPlay',
    container: 'mkv',
    videoTrackId: 'video-1',
    audioTrackId: 'audio-en',
    audioTrackPosition: 0,
    audioCodec: 'truehd',
    subtitleTrackId: null,
    subtitleTrackPosition: null,
};

function fakeEngine(kind: 'web' | 'desktop', switchesLocally: boolean) {
    const events = new EngineEventHub();
    const engine: PlaybackEngine = {
        kind,
        load: vi.fn(() => Promise.resolve()),
        unload: vi.fn(),
        selectLocalTracks: vi.fn(() => switchesLocally),
        canShowTextSubtitles: () => true,
        showTextSubtitle: vi.fn(() => Promise.resolve('shown' as const)),
        hideTextSubtitle: vi.fn(),
        setChromeState: vi.fn(),
        setScrubSprite: vi.fn(),
        read: () => ({ engine: kind, droppedFrames: null, totalFrames: null, bufferedSeconds: null, bandwidthKbps: null }),
        on: handler => events.add(handler),
    };
    return { engine, events };
}

interface PlayerState {
    player: PlayerContextType;
    time: { currentTime: number; duration: number };
}

function playerState(media: PlayableMedia): PlayerState {
    return {
        player: {
            currentMedia: media,
            isPlaying: true,
            isMinimized: false,
            volume: 1,
            sessionId: media.sessionId ?? null,
            playMedia: vi.fn(),
            playQueue: vi.fn(),
            addToQueue: vi.fn(),
            playNext: vi.fn(),
            nextTrack: vi.fn(),
            previousTrack: vi.fn(),
            jumpToQueueIndex: vi.fn(),
            hasNext: false,
            hasPrevious: false,
            queue: [],
            queueIndex: 0,
            isShuffled: false,
            toggleShuffle: vi.fn(),
            repeatMode: 'off',
            cycleRepeatMode: vi.fn(),
            togglePlayPause: vi.fn(),
            seek: vi.fn(),
            skipForward: vi.fn(),
            skipBackward: vi.fn(),
            setMinimized: vi.fn(),
            isFullscreen: false,
            toggleFullscreen: vi.fn(),
            setFullscreen: vi.fn(),
            closePlayer: vi.fn(),
            setVolume: vi.fn(),
            changeStreams: vi.fn(() => Promise.resolve()),
            videoRef: { current: null },
            radioSeed: null,
            radioLabel: null,
            startRadio: vi.fn(),
            getAudioAnalyser: () => null,
        },
        time: { currentTime: 0, duration: 2700 },
    };
}

function renderController(engine: PlaybackEngine, state: PlayerState) {
    const current = { state };
    const wrapper = ({ children }: { children: ReactNode }) => (
        <MemoryRouter>
            <PlayerContext.Provider value={current.state.player}>
                <PlayerTimeContext.Provider value={current.state.time}>{children}</PlayerTimeContext.Provider>
            </PlayerContext.Provider>
        </MemoryRouter>
    );
    const view = renderHook(() => usePlaybackController(engine), { wrapper });
    const at = (currentTime: number) => {
        current.state = { ...current.state, time: { ...current.state.time, currentTime } };
        view.rerender();
    };
    return { ...view, at };
}

const intro: MediaMarker = { type: 'Intro', startSeconds: 60, endSeconds: 150, order: 0 };

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(mediaService.getMediaItem).mockResolvedValue(details);
    vi.mocked(profileService.getMyPlaybackPreferences).mockResolvedValue({ autoSkipIntro: true, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15 });
});

describe('the playback controller', () => {
    it('loads the stream at the resume position the session starts from', () => {
        const { engine } = fakeEngine('desktop', true);

        renderController(engine, playerState(episode));

        expect(engine.load).toHaveBeenCalledWith(expect.objectContaining({
            url: episode.streamUrl,
            startPosition: 754,
            strategy: 'DirectPlay',
            title: 'Pilot',
            audioTrack: { position: 0, codec: 'truehd', language: null },
            subtitleTrack: null,
        }));
    });

    it('loads the next stream into the same engine when the session changes', () => {
        const { engine } = fakeEngine('desktop', true);
        const state = playerState(episode);
        const view = renderController(engine, state);

        state.player = { ...state.player, currentMedia: { ...episode, sessionId: 'session-2', streamUrl: '/api/streaming/play/session-2?t=signed', startPosition: 0 }, sessionId: 'session-2' };
        view.rerender();

        expect(engine.unload).toHaveBeenCalledTimes(1);
        expect(engine.load).toHaveBeenCalledTimes(2);
        expect(engine.load).toHaveBeenLastCalledWith(expect.objectContaining({ startPosition: 0 }));
    });

    it('auto-skips an intro once, however often the clock ticks inside it', async () => {
        const { engine } = fakeEngine('web', false);
        const state = playerState({ ...episode, skipMarkers: [intro] });
        const view = renderController(engine, state);
        await waitFor(() => expect(profileService.getMyPlaybackPreferences).toHaveBeenCalled());
        await act(async () => { await Promise.resolve(); });

        view.at(61);
        view.at(62);
        view.at(63);

        expect(state.player.seek).toHaveBeenCalledTimes(1);
        expect(state.player.seek).toHaveBeenCalledWith(150);
    });

    it('auto-skips the intro again after leaving it and coming back', async () => {
        const { engine } = fakeEngine('web', false);
        const state = playerState({ ...episode, skipMarkers: [intro] });
        const view = renderController(engine, state);
        await act(async () => { await Promise.resolve(); });

        view.at(61);
        view.at(200);
        view.at(70);

        expect(state.player.seek).toHaveBeenCalledTimes(2);
    });

    it('hands off to the next episode: last ping, stop, a new session from the start', async () => {
        const { engine } = fakeEngine('desktop', true);
        const state = playerState(episode);
        vi.mocked(streamingService.startSession).mockResolvedValue({
            sessionId: 'session-2', streamUrl: '/api/streaming/play/session-2?t=signed', videoTrackId: 'video-2', audioTrackId: 'audio-2',
            subtitleTrackId: null, strategy: 'DirectPlay', videoStrategy: 'DirectPlay', audioStrategy: 'DirectPlay', subtitleStrategy: null,
            videoCodec: 'hevc', audioCodec: 'eac3', targetAudioChannels: 6, container: 'mkv', bandwidthKbps: 0,
        });
        const view = renderController(engine, state);
        view.at(2690);
        const next: UpNextItemVM = { id: 'episode-2', title: 'Second', type: 'Episode', seasonNumber: 1, episodeNumber: 2, tvShowTitle: 'The Show' };

        await act(() => view.result.current.playNext(next));

        expect(streamingService.pingSession).toHaveBeenCalledWith('session-1', 2690, 2700, true, undefined);
        expect(streamingService.stopSession).toHaveBeenCalledWith('session-1', undefined);
        expect(streamingService.startSession).toHaveBeenCalledWith('episode-2', expect.any(String), 0, undefined, undefined, undefined, undefined);
        expect(state.player.playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'episode-2', sessionId: 'session-2', startPosition: 0 }));
    });

    it('switches audio inside the file when the engine can, without a new session', async () => {
        const { engine } = fakeEngine('desktop', true);
        const state = playerState(episode);
        const view = renderController(engine, state);
        await waitFor(() => expect(view.result.current.mediaDetails).not.toBeNull());

        await act(() => view.result.current.selectAudio('audio-ja'));

        expect(engine.selectLocalTracks).toHaveBeenCalledWith({ audioTrack: { position: 1, codec: 'eac3', language: 'jpn' } });
        expect(state.player.changeStreams).not.toHaveBeenCalled();
        expect(view.result.current.playingAudioId).toBe('audio-ja');
    });

    it('restarts the session for an audio change the engine cannot make', async () => {
        const { engine } = fakeEngine('web', false);
        const state = playerState(episode);
        const view = renderController(engine, state);
        await waitFor(() => expect(view.result.current.mediaDetails).not.toBeNull());
        act(() => view.result.current.setSelVideo('video-1'));

        await act(() => view.result.current.selectAudio('audio-ja'));

        expect(state.player.changeStreams).toHaveBeenCalledWith('video-1', 'audio-ja', 'none');
    });

    it('selects an embedded subtitle in the file when the engine can, image subtitles too', async () => {
        const { engine } = fakeEngine('desktop', true);
        const state = playerState(episode);
        const view = renderController(engine, state);
        await waitFor(() => expect(view.result.current.mediaDetails).not.toBeNull());

        await act(() => view.result.current.selectSubtitle('sub-pgs'));

        expect(engine.selectLocalTracks).toHaveBeenCalledWith({ subtitleTrack: { position: 0, codec: 'hdmv_pgs_subtitle', language: 'eng' } });
        expect(state.player.changeStreams).not.toHaveBeenCalled();
        expect(view.result.current.effectiveSubtitleId).toBe('sub-pgs');
    });

    it('burns an image subtitle in with a new session when the engine cannot draw it', async () => {
        const { engine } = fakeEngine('web', false);
        const state = playerState(episode);
        const view = renderController(engine, state);
        await waitFor(() => expect(view.result.current.mediaDetails).not.toBeNull());
        const onStreamRestart = vi.fn();

        await act(() => view.result.current.selectSubtitle('sub-pgs', { onStreamRestart }));

        expect(onStreamRestart).toHaveBeenCalled();
        expect(state.player.changeStreams).toHaveBeenCalledWith(expect.any(String), expect.any(String), 'sub-pgs');
    });

    it('loads a sidecar subtitle as text, whatever the engine', async () => {
        const { engine } = fakeEngine('desktop', true);
        const state = playerState(episode);
        const view = renderController(engine, state);
        await waitFor(() => expect(view.result.current.mediaDetails).not.toBeNull());

        await act(() => view.result.current.selectSubtitle('sub-srt'));

        expect(engine.showTextSubtitle).toHaveBeenCalledWith(
            expect.objectContaining({ sessionId: 'session-1', trackId: 'sub-srt', track: null }),
            expect.any(Function),
        );
        expect(state.player.changeStreams).not.toHaveBeenCalled();
    });

    it('treats the end of the file reported by the engine as finished', () => {
        const { engine, events } = fakeEngine('desktop', true);
        const view = renderController(engine, playerState(episode));

        act(() => events.emit({ type: 'ended' }));

        expect(view.result.current.finished).toBe(true);
    });
});
