import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PlayerContext, PlayerTimeContext, type PlayableMedia, type PlayerContextType } from '../../contexts/usePlayer';
import { PlaybackTransportContext } from '../../contexts/usePlaybackTransport';
import { fakeDesktopBridge, type FakeDesktopBridge } from '../../test/fakeDesktopBridge';
import { getDesktopPlaybackState, loadDesktopPlayback, releaseBuiltInPlayer } from '../../desktop/desktopPlayback';
import type { PlaybackTransport } from './engine/PlaybackEngine';
import DesktopPlaybackHost, { CHROME_STATE_INTERVAL_MS } from './DesktopPlaybackHost';

const confirm = vi.fn(() => Promise.resolve(true));

vi.mock('../../api/Media/mediaService', () => ({
    mediaService: {
        getMarkers: vi.fn(() => Promise.resolve([])),
        getMediaItem: vi.fn(() => Promise.resolve({ id: 'movie', title: 'Feature', type: 'Movie', libraryId: 'library', collectionIds: [] })),
        getUpNext: vi.fn(() => Promise.resolve({ relatedLists: [] })),
    },
}));
vi.mock('../../api/Users/profileService', () => ({
    profileService: { getMyPlaybackPreferences: vi.fn(() => Promise.resolve({ autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15 })) },
}));
vi.mock('../../api/Streaming/streamingService', () => ({
    streamingService: { pingSession: vi.fn(), stopSession: vi.fn(), startSession: vi.fn(), rememberChoice: vi.fn(() => Promise.resolve({})), releaseSubtitleUrls: vi.fn() },
}));
vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('../../hooks/useFeatureFlags', () => ({ useFeatureFlags: () => ({ subtitleSearch: false }) }));
vi.mock('../../hooks/useVideoThumbnails', () => ({
    useVideoThumbnails: () => ({ available: false, cues: [], width: 0, height: 0, spriteUrl: '', findCue: () => null }),
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm, prompt: () => Promise.resolve(null) }),
}));

const movie: PlayableMedia = {
    id: 'movie',
    title: 'Feature',
    streamUrl: '/api/streaming/play/session-1?t=signed',
    sessionId: 'session-1',
    startPosition: 120,
    strategy: 'DirectPlay',
    videoTrackId: 'video-1',
    audioTrackId: 'audio-1',
};

function player(): PlayerContextType {
    return {
        currentMedia: movie, isPlaying: true, isMinimized: false, volume: 1, sessionId: 'session-1',
        playMedia: vi.fn(), playQueue: vi.fn(), addToQueue: vi.fn(), playNext: vi.fn(), nextTrack: vi.fn(), previousTrack: vi.fn(),
        jumpToQueueIndex: vi.fn(), hasNext: false, hasPrevious: false, queue: [], queueIndex: 0, isShuffled: false, toggleShuffle: vi.fn(),
        repeatMode: 'off', cycleRepeatMode: vi.fn(), togglePlayPause: vi.fn(), seek: vi.fn(), skipForward: vi.fn(), skipBackward: vi.fn(),
        setMinimized: vi.fn(), isFullscreen: false, toggleFullscreen: vi.fn(), setFullscreen: vi.fn(), closePlayer: vi.fn(() => Promise.resolve()),
        setVolume: vi.fn(), changeStreams: vi.fn(() => Promise.resolve()), videoRef: { current: null }, radioSeed: null, radioLabel: null,
        startRadio: vi.fn(), getAudioAnalyser: () => null,
    };
}

function renderHost(fake: FakeDesktopBridge, context: PlayerContextType = player()) {
    const attached: PlaybackTransport[] = [];
    const attachTransport = vi.fn((transport: PlaybackTransport) => {
        attached.push(transport);
        return () => { };
    });
    const view = render(
        <MemoryRouter>
            <PlayerContext.Provider value={context}>
                <PlayerTimeContext.Provider value={{ currentTime: 130, duration: 7200 }}>
                    <PlaybackTransportContext.Provider value={{ attachTransport }}>
                        <DesktopPlaybackHost bridge={fake.bridge} />
                    </PlaybackTransportContext.Provider>
                </PlayerTimeContext.Provider>
            </PlayerContext.Provider>
        </MemoryRouter>,
    );
    return { ...view, context, attached };
}

beforeEach(() => {
    vi.clearAllMocks();
    confirm.mockResolvedValue(true);
});

afterEach(() => {
    releaseBuiltInPlayer();
    delete window.voraDesktop;
    vi.useRealTimers();
});

describe('the desktop playback host', () => {
    it('hands its engine to the player and opens mpv at the resume position', async () => {
        const fake = fakeDesktopBridge();
        const { attached } = renderHost(fake);

        expect(attached).toHaveLength(1);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalledWith(expect.objectContaining({ startSeconds: 120, title: 'Feature' })));
    });

    it('closes mpv when it goes away', async () => {
        const fake = fakeDesktopBridge();
        const { unmount } = renderHost(fake);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalled());

        unmount();

        expect(fake.bridge.player.close).toHaveBeenCalled();
    });

    it('turns what the viewer does in the mpv chrome into the web player calls', async () => {
        const fake = fakeDesktopBridge();
        const { context } = renderHost(fake);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalled());

        act(() => fake.emit({ type: 'intent', intent: { type: 'toggle-pause' } }));
        act(() => fake.emit({ type: 'intent', intent: { type: 'seek', to: 900 } }));
        act(() => fake.emit({ type: 'intent', intent: { type: 'exit' } }));

        expect(context.togglePlayPause).toHaveBeenCalled();
        expect(context.seek).toHaveBeenCalledWith(900);
        expect(context.closePlayer).toHaveBeenCalled();
    });

    it('offers the built-in player when mpv cannot start, and restarts the session there', async () => {
        const fake = fakeDesktopBridge();
        window.voraDesktop = fake.bridge;
        await loadDesktopPlayback();
        const { context } = renderHost(fake);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalled());

        act(() => fake.emit({ type: 'error', message: 'vo/gpu-next: could not create a Vulkan device' }));

        await waitFor(() => expect(context.changeStreams).toHaveBeenCalledWith('video-1', 'audio-1', 'none'));
        expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: "Couldn't start the Vora player — play with the built-in player instead?" }));
        expect(getDesktopPlaybackState().builtInPlayerForced).toBe(true);
        expect(fake.bridge.player.close).toHaveBeenCalled();
    });

    it('closes the player when the viewer turns the built-in player down', async () => {
        confirm.mockResolvedValue(false);
        const fake = fakeDesktopBridge();
        const { context } = renderHost(fake);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalled());

        act(() => fake.emit({ type: 'error', message: 'mpv crashed' }));

        await waitFor(() => expect(context.closePlayer).toHaveBeenCalled());
        expect(context.changeStreams).not.toHaveBeenCalled();
    });

    it('sends the chrome state at most ten times a second, and only when it changed', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        const fake = fakeDesktopBridge();
        renderHost(fake);
        await waitFor(() => expect(fake.bridge.player.open).toHaveBeenCalled());
        await act(async () => { await Promise.resolve(); });

        act(() => { vi.advanceTimersByTime(CHROME_STATE_INTERVAL_MS); });
        const sent = vi.mocked(fake.bridge.player.setChromeState).mock.calls.length;
        expect(sent).toBeGreaterThan(0);
        expect(vi.mocked(fake.bridge.player.setChromeState).mock.calls[0][0]).toMatchObject({ title: 'Feature', status: 'loading' });

        act(() => { vi.advanceTimersByTime(CHROME_STATE_INTERVAL_MS * 5); });
        expect(vi.mocked(fake.bridge.player.setChromeState).mock.calls.length).toBe(sent);
    });
});
