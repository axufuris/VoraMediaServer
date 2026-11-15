import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import LiveRadioPlayer from './LiveRadioPlayer';
import type { PlayableMedia } from '../../contexts/usePlayer';
import { serverVault, type VoraServer } from '../../utils/serverVault';

const mocks = vi.hoisted(() => ({
    saveEpisodeState: vi.fn(),
    resolveEpisodeAudio: vi.fn(),
    media: null as PlayableMedia | null,
    video: { current: null as HTMLVideoElement | null },
}));

vi.mock('../../api/Podcasts/podcastService', () => ({ podcastService: { saveEpisodeState: mocks.saveEpisodeState, resolveEpisodeAudio: mocks.resolveEpisodeAudio } }));
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
    mocks.resolveEpisodeAudio.mockReset();
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

// The Boyscast's enclosure is podtrac -> pscrb.fm -> clrtpod.com -> mgln.ai ->
// arttrk.com -> prfx.byspotify.com -> traffic.libsyn.com -> the file. Behind
// uBlock Origin the request dies at arttrk.com and the element reports a media
// error; the episode used to sit there, never starting and never saying why.
describe('LiveRadioPlayer podcast address fallback', () => {
    const resolved = 'https://delivery-edge.libsyn.com/shows/547682/items/43744024/ep375.mp3';
    const ERROR = /This episode could not be played/;

    const failLoad = () => {
        const video = mocks.video.current;
        if (!video) throw new Error('no video element');
        video.dispatchEvent(new Event('error'));
        return video;
    };

    it('plays where the server says the redirects end when the feed address fails', async () => {
        mocks.resolveEpisodeAudio.mockResolvedValue(resolved);
        mocks.media = episodeFrom('cabin');
        render(<LiveRadioPlayer />);

        const video = failLoad();

        await waitFor(() => expect(video.src).toBe(resolved));
        expect(mocks.resolveEpisodeAudio).toHaveBeenCalledWith('garden-27', 'cabin');
        expect(screen.queryByText(ERROR)).toBeNull();
    });

    it('says the episode could not be played when the resolved address fails too', async () => {
        mocks.resolveEpisodeAudio.mockResolvedValue(resolved);
        mocks.media = episodeFrom('home');
        render(<LiveRadioPlayer />);

        const video = failLoad();
        await waitFor(() => expect(video.src).toBe(resolved));
        failLoad();

        expect(await screen.findByText(ERROR)).toBeTruthy();
        expect(mocks.resolveEpisodeAudio).toHaveBeenCalledTimes(1);
    });

    it('says so when the server cannot resolve the address either', async () => {
        mocks.resolveEpisodeAudio.mockRejectedValue(new Error('502'));
        mocks.media = episodeFrom('home');
        render(<LiveRadioPlayer />);

        failLoad();

        expect(await screen.findByText(ERROR)).toBeTruthy();
    });

    it('does not retry an address the server resolves to itself', async () => {
        mocks.resolveEpisodeAudio.mockResolvedValue('https://cdn.example.com/garden-27.mp3');
        mocks.media = episodeFrom('home');
        render(<LiveRadioPlayer />);

        const video = failLoad();

        expect(await screen.findByText(ERROR)).toBeTruthy();
        expect(video.src).toBe('https://cdn.example.com/garden-27.mp3');
    });

    it('leaves music to its own stream', () => {
        mocks.media = { ...episodeFrom('home'), playbackContextType: 'Music' };
        render(<LiveRadioPlayer />);

        failLoad();

        expect(mocks.resolveEpisodeAudio).not.toHaveBeenCalled();
    });
});

// Music connects the element to the EQ's Web Audio graph for good, and an
// episode from another site played through it comes out silent.
describe('LiveRadioPlayer audio element', () => {
    it('gives an episode a new element after music', () => {
        mocks.media = { ...episodeFrom('home'), id: 'track-1', playbackContextType: 'Music' };
        const { rerender } = render(<LiveRadioPlayer />);
        const musicElement = mocks.video.current;

        mocks.media = episodeFrom('home');
        rerender(<LiveRadioPlayer />);

        expect(mocks.video.current).toBeInstanceOf(HTMLVideoElement);
        expect(mocks.video.current).not.toBe(musicElement);
    });

    it('keeps the element from one episode to the next', () => {
        mocks.media = episodeFrom('home');
        const { rerender } = render(<LiveRadioPlayer />);
        const first = mocks.video.current;

        mocks.media = { ...episodeFrom('home'), id: 'garden-28' };
        rerender(<LiveRadioPlayer />);

        expect(mocks.video.current).toBe(first);
    });
});

