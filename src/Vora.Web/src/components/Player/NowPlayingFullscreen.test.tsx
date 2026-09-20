import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import NowPlayingFullscreen from './NowPlayingFullscreen';
import { PlayerContext, PlayerTimeContext, type PlayableMedia, type PlayerContextType } from '../../contexts/usePlayer';
import type { LyricsVM } from '../../api/Music/musicService';

const getTrackLyrics = vi.fn<(trackId: string, serverId?: string) => Promise<LyricsVM | null>>();
const addToPlaylist = vi.fn<(...args: unknown[]) => Promise<void>>(() => Promise.resolve());

vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

// jsdom has no scrollTo on elements; the synced-lyrics auto-scroll calls it.
Element.prototype.scrollTo = vi.fn();

vi.mock('../../api/Collections/playlistService', () => ({
    playlistService: {
        getPlaylists: () => Promise.resolve([{ id: 'pl-1', name: 'Road Trip', mediaType: 'Music', itemCount: 4, posterUrls: [], backdropUrls: [] }]),
        getPlaylistsContainingItem: () => Promise.resolve([]),
        addToPlaylist: (...args: unknown[]) => addToPlaylist(...args),
        removeMediaFromPlaylist: () => Promise.resolve(),
    },
}));

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

    it('offers the current track to a playlist, the way the album menu does', async () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player());

        fireEvent.click(screen.getByRole('button', { name: 'Playlist' }));

        expect(await screen.findByText('Road Trip')).toBeInTheDocument();
        fireEvent.click(screen.getByText('Road Trip'));
        await waitFor(() => expect(addToPlaylist).toHaveBeenCalledWith('pl-1', 'carousel', undefined));
    });

    it('has no playlist button while a radio station is playing', () => {
        getTrackLyrics.mockResolvedValue(null);
        renderScreen(player({ radioSeed: { seedKind: 'Artist', seedArtistId: 'a1' }, radioLabel: 'blink-182 radio' }));

        expect(screen.queryByRole('button', { name: 'Playlist' })).toBeNull();
    });

    it('lets plain lyrics ride along with the song instead of being scrolled', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: `Line one
Line two
Line three`, syncedLyrics: null, isSynced: false, providerName: 'LRClib', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        const panel = await screen.findByTestId('lyrics-panel');
        expect(panel.className).toContain('overflow-hidden');
        expect(panel.className).not.toContain('overflow-y-auto');
        expect(panel).not.toHaveAttribute('tabindex');
    });

    it('keeps synced lyrics scrollable so a line can be clicked', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: null, syncedLyrics: `[00:10.00]Line one
[00:20.00]Line two`, isSynced: true, providerName: 'LRClib', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        const panel = await screen.findByTestId('lyrics-panel');
        expect(panel.className).toContain('overflow-y-auto');
    });
});
