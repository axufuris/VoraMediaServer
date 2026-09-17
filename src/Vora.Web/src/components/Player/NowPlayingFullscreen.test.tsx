import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import NowPlayingFullscreen from './NowPlayingFullscreen';
import { PlayerContext, PlayerTimeContext, type PlayableMedia, type PlayerContextType } from '../../contexts/usePlayer';
import type { LyricsVM } from '../../api/Music/musicService';

const getTrackLyrics = vi.fn<(trackId: string, serverId?: string) => Promise<LyricsVM | null>>();

vi.mock('../../api/Music/musicService', () => ({
    musicService: {
        getLikedTracks: () => Promise.resolve({ tracks: [] }),
        getTrackLyrics: (trackId: string, serverId?: string) => getTrackLyrics(trackId, serverId),
        likeTrack: () => Promise.resolve(),
        unlikeTrack: () => Promise.resolve(),
        saveStation: () => Promise.resolve(),
    },
}));

const track: PlayableMedia = {
    id: 'carousel',
    title: 'Carousel',
    subtitle: 'blink-182 — Cheshire Cat',
    streamUrl: 'https://example.test/carousel.mp3',
    playbackContextType: 'Music',
};

const player = (overrides: Partial<PlayerContextType> = {}): PlayerContextType => ({
    currentMedia: track,
    isPlaying: true,
    isMinimized: true,
    volume: 0.8,
    sessionId: null,
    playMedia: vi.fn(),
    playQueue: vi.fn(),
    addToQueue: vi.fn(),
    playNext: vi.fn(),
    nextTrack: vi.fn(),
    previousTrack: vi.fn(),
    jumpToQueueIndex: vi.fn(),
    hasNext: true,
    hasPrevious: false,
    queue: [track],
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
    isFullscreen: true,
    toggleFullscreen: vi.fn(),
    setFullscreen: vi.fn(),
    closePlayer: vi.fn(),
    setVolume: vi.fn(),
    changeStreams: vi.fn(),
    videoRef: { current: null },
    radioSeed: null,
    radioLabel: null,
    startRadio: vi.fn(),
    ...overrides,
});

const renderScreen = (value: PlayerContextType) => render(
    <MemoryRouter>
        <PlayerContext.Provider value={value}>
            <PlayerTimeContext.Provider value={{ currentTime: 30, duration: 191 }}>
                <div data-vora-client="">
                    <NowPlayingFullscreen />
                </div>
            </PlayerTimeContext.Provider>
        </PlayerContext.Provider>
    </MemoryRouter>,
);

describe('music now playing', () => {
    beforeEach(() => {
        getTrackLyrics.mockReset();
    });

    it('renders nothing unless it is open for music', () => {
        getTrackLyrics.mockResolvedValue(null);
        const { container } = renderScreen(player({ isFullscreen: false }));

        expect(container.querySelector('header')).toBeNull();
    });

    it('keeps the music transport and the seek bar', () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player());

        expect(screen.getByRole('slider', { name: 'Playback position' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
        expect(screen.getByRole('button', { name: 'Shuffle: off' })).toHaveAttribute('aria-pressed', 'false');
    });

    it('labels the panel toggles', () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player());

        expect(screen.getByRole('button', { name: 'Queue' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Audio' })).toBeInTheDocument();
        expect(screen.getByRole('slider', { name: 'Volume' })).toBeInTheDocument();
    });

    it('shows the lyrics toggle once the track turns out to have lyrics', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: 'I talk to you every now and then', syncedLyrics: null, isSynced: false, providerName: 'LRClib', sourceUrl: null });
        renderScreen(player());

        expect(await screen.findByRole('button', { name: 'Lyrics' })).toHaveAttribute('aria-pressed', 'false');
    });

    it('has no lyrics toggle for a track without lyrics', async () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player());

        await waitFor(() => expect(getTrackLyrics).toHaveBeenCalled());
        expect(screen.queryByRole('button', { name: 'Lyrics' })).toBeNull();
    });

    it('opens the audio settings above the control bar', () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player());

        fireEvent.click(screen.getByRole('button', { name: 'Audio' }));

        expect(document.getElementById('now-playing-audio-settings')).not.toBeNull();
    });

    it('minimizes on Escape', () => {
        getTrackLyrics.mockResolvedValue(null);
        const value = player();
        renderScreen(value);

        fireEvent.keyDown(window, { key: 'Escape' });

        expect(value.setFullscreen).toHaveBeenCalledWith(false);
    });

    it('offers to save a radio as a station', () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player({ radioSeed: { seedKind: 'Artist', seedArtistId: 'blink' }, radioLabel: 'blink-182 Radio' }));

        expect(screen.getByText('blink-182 Radio')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Save station/ })).toBeInTheDocument();
    });
});
