import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render } from '@testing-library/react';
import LiveRadioPlayer from './LiveRadioPlayer';
import type { PlayableMedia } from '../../contexts/usePlayer';
import { serverVault, type VoraServer } from '../../utils/serverVault';

const mocks = vi.hoisted(() => ({
    saveEpisodeState: vi.fn(),
    media: null as PlayableMedia | null,
    video: { current: null as HTMLVideoElement | null },
}));

vi.mock('../../api/Podcasts/podcastService', () => ({ podcastService: { saveEpisodeState: mocks.saveEpisodeState } }));
vi.mock('../../contexts/usePlayer', () => ({
    usePlayer: () => ({
        currentMedia: mocks.media,
        isPlaying: true,
        isMinimized: true,
        volume: 1,
        videoRef: mocks.video,
        togglePlayPause: () => { },
        setMinimized: () => { },
        closePlayer: () => { },
        setVolume: () => { },
        playMedia: () => { },
        skipForward: () => { },
        skipBackward: () => { },
        seek: () => { },
        nextTrack: () => { },
        previousTrack: () => { },
        hasNext: false,
        hasPrevious: false,
        setFullscreen: () => { },
        isFullscreen: false,
    }),
    usePlayerTime: () => ({ currentTime: 0, duration: 0 }),
}));

const server = (id: string, name: string): VoraServer =>
    ({ id, name, url: `https://${id}.example.com`, token: `tok-${id}`, profileId: `prof-${id}`, isAdmin: false });

const episodeFrom = (serverId?: string): PlayableMedia => ({
    id: 'garden-27',
    title: 'Planting garlic',
    subtitle: 'Gardening Hour',
    streamUrl: 'https://cdn.example.com/garden-27.mp3',
    serverId,
    container: 'audio',
    playbackContextType: 'Podcast',
});

const pauseAt = (seconds: number) => {
    const video = mocks.video.current;
    if (!video) throw new Error('no video element');
    Object.defineProperty(video, 'currentTime', { value: seconds, configurable: true, writable: true });
    video.dispatchEvent(new Event('pause'));
};

beforeEach(() => {
    localStorage.clear();
    serverVault.addOrUpdateServer(server('home', 'Home'));
    serverVault.addOrUpdateServer(server('cabin', 'Cabin'));
    serverVault.setActiveServerId('home');
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve());
    mocks.saveEpisodeState.mockResolvedValue(undefined);
    mocks.video.current = document.createElement('video');
});

afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    mocks.saveEpisodeState.mockReset();
});

describe('LiveRadioPlayer podcast progress', () => {
    it('saves progress on the server the episode came from', () => {
        mocks.media = episodeFrom('cabin');
        render(<LiveRadioPlayer />);

        pauseAt(42);

        expect(mocks.saveEpisodeState).toHaveBeenCalledWith('garden-27', 42, undefined, 'cabin');
    });

    it('saves on the active server when the episode names none', () => {
        mocks.media = episodeFrom(undefined);
        render(<LiveRadioPlayer />);

        pauseAt(42);

        expect(mocks.saveEpisodeState).toHaveBeenCalledWith('garden-27', 42, undefined, 'home');
    });
});
