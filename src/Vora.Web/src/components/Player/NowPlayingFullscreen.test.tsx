import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import NowPlayingFullscreen from './NowPlayingFullscreen';
import { PlayerContext, PlayerTimeContext, type PlayableMedia, type PlayerContextType } from '../../contexts/usePlayer';
import type { LyricsVM, TrackInfoVM } from '../../api/Music/musicService';

const getTrackLyrics = vi.fn<(trackId: string, serverId?: string) => Promise<LyricsVM | null>>();
const getTrackInfo = vi.fn<(trackId: string, serverId?: string) => Promise<TrackInfoVM | null>>(() => Promise.resolve(null));
const addToPlaylist = vi.fn<(...args: unknown[]) => Promise<void>>(() => Promise.resolve());
const createPlaylistFromQueue = vi.fn<(name: string, ids: string[], serverId?: string) => Promise<{ id: string }>>(() => Promise.resolve({ id: 'new' }));
const prompt = vi.fn<() => Promise<string | null>>(() => Promise.resolve(null));

vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => prompt() }),
}));

// jsdom has no scrollTo on elements; the synced-lyrics auto-scroll calls it.
Element.prototype.scrollTo = vi.fn();
HTMLCanvasElement.prototype.getContext = vi.fn(() => null) as unknown as HTMLCanvasElement['getContext'];

vi.mock('../../api/Collections/playlistService', () => ({
    playlistService: {
        getPlaylists: () => Promise.resolve([{ id: 'pl-1', name: 'Road Trip', mediaType: 'Music', itemCount: 4, posterUrls: [], backdropUrls: [] }]),
        getPlaylistsContainingItem: () => Promise.resolve([]),
        addToPlaylist: (...args: unknown[]) => addToPlaylist(...args),
        createPlaylistFromQueue: (name: string, ids: string[], serverId?: string) => createPlaylistFromQueue(name, ids, serverId),
        removeMediaFromPlaylist: () => Promise.resolve(),
    },
}));

vi.mock('../../api/Music/musicService', () => ({
    musicService: {
        getLikedTracks: () => Promise.resolve({ tracks: [] }),
        getTrackLyrics: (trackId: string, serverId?: string) => getTrackLyrics(trackId, serverId),
        getTrackInfo: (trackId: string, serverId?: string) => getTrackInfo(trackId, serverId),
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
    getAudioAnalyser: () => null,
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
        getTrackInfo.mockResolvedValue(null);
        localStorage.clear();
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

    // Plain lyrics have no timings. Scrolling them in proportion to the song
    // read as broken sync and locked out manual scrolling, so they are a plain
    // block of text the listener moves through themselves.
    it('shows plain lyrics as text the listener scrolls, not as fake sync', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: `[Chorus]
Line one
Line two`, syncedLyrics: null, isSynced: false, providerName: 'Genius', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        const panel = await screen.findByTestId('lyrics-panel');
        expect(panel.className).toContain('overflow-y-auto');
        expect(panel.className).not.toContain('overflow-hidden');
        // Scrollable by keyboard as well as by pointer.
        expect(panel).toHaveAttribute('tabindex', '0');
    });

    it('says plainly when lyrics are not synced', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: 'Line one', syncedLyrics: null, isSynced: false, providerName: 'Genius', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        expect(await screen.findByText('Not synced to this recording')).toBeInTheDocument();
    });

    it('does not label synced lyrics as unsynced', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: null, syncedLyrics: `[00:10.00]Line one`, isSynced: true, providerName: 'LRClib', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        await screen.findByText('Line one');
        expect(screen.queryByText('Not synced to this recording')).toBeNull();
    });

    it('keeps synced lyrics scrollable so a line can be clicked', async () => {
        getTrackLyrics.mockResolvedValue({ plainLyrics: null, syncedLyrics: `[00:10.00]Line one
[00:20.00]Line two`, isSynced: true, providerName: 'LRClib', sourceUrl: null });
        renderScreen(player());

        fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

        const panel = await screen.findByTestId('lyrics-panel');
        expect(panel.className).toContain('overflow-y-auto');
    });

    it('saves the songs in the queue as a playlist, in queue order', async () => {
        getTrackLyrics.mockResolvedValue(null);
        prompt.mockResolvedValueOnce('Saturday queue');
        const next: PlayableMedia = { ...track, id: 'all-the-small-things', title: 'All the Small Things' };
        const podcast: PlayableMedia = { ...track, id: 'episode', title: 'An episode', playbackContextType: 'Podcast' };
        renderScreen(player({ queue: [track, next, podcast] }));

        fireEvent.click(screen.getByRole('button', { name: 'Queue' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save as playlist' }));

        await waitFor(() => expect(createPlaylistFromQueue).toHaveBeenCalledWith('Saturday queue', ['carousel', 'all-the-small-things'], undefined));
    });

    it("shows the song's quality and how it feels under the artwork", async () => {
        getTrackLyrics.mockResolvedValue(null);
        getTrackInfo.mockResolvedValue({
            id: track.id,
            quality: { format: 'FLAC', sampleRate: 96000, bitrate: null, lossless: true, hiRes: true, label: 'Hi-Res · FLAC 96 kHz' },
            moods: ['euphoric', 'upbeat'],
            energy: 'High',
            themes: [],
            goodFor: ['party'],
            isInstrumental: false,
        });
        renderScreen(player());

        const info = await screen.findByTestId('now-playing-track-info');
        expect(info).toHaveTextContent('Hi-Res · FLAC 96 kHz');
        expect(info).toHaveTextContent('Euphoric · Upbeat · High energy');
        expect(getTrackInfo).toHaveBeenCalledWith(track.id, undefined);
    });

    describe('lyrics and synth stay as the listener left them', () => {
        const withLyrics: LyricsVM = { plainLyrics: 'I talk to you every now and then', syncedLyrics: null, isSynced: false, providerName: 'LRClib', sourceUrl: null };

        it('remembers lyrics turned on after the screen closes and opens again', async () => {
            getTrackLyrics.mockResolvedValue(withLyrics);
            const first = renderScreen(player());

            fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));
            expect(await screen.findByTestId('lyrics-panel')).toBeInTheDocument();
            expect(localStorage.getItem('now_playing_lyrics')).toBe('true');
            first.unmount();

            renderScreen(player());

            expect(await screen.findByTestId('lyrics-panel')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Lyrics' })).toHaveAttribute('aria-pressed', 'true');
        });

        it('remembers lyrics turned off too', async () => {
            localStorage.setItem('now_playing_lyrics', 'true');
            getTrackLyrics.mockResolvedValue(withLyrics);
            renderScreen(player());

            fireEvent.click(await screen.findByRole('button', { name: 'Lyrics' }));

            await waitFor(() => expect(screen.queryByTestId('lyrics-panel')).not.toBeInTheDocument());
            expect(localStorage.getItem('now_playing_lyrics')).toBe('false');
        });

        it('turns the synth on and keeps it on', async () => {
            getTrackLyrics.mockResolvedValue(null);
            const first = renderScreen(player());

            fireEvent.click(screen.getByRole('button', { name: 'Synth' }));

            expect(screen.getByTestId('synth-panel')).toBeInTheDocument();
            expect(screen.getByRole('img', { name: 'Music visualizer' })).toBeInTheDocument();
            expect(localStorage.getItem('now_playing_synth')).toBe('true');
            first.unmount();

            renderScreen(player());
            expect(screen.getByTestId('synth-panel')).toBeInTheDocument();
        });

        it('puts the lyrics in a smaller panel under the synth when both are on', async () => {
            localStorage.setItem('now_playing_lyrics', 'true');
            localStorage.setItem('now_playing_synth', 'true');
            getTrackLyrics.mockResolvedValue(withLyrics);
            renderScreen(player());

            const lyrics = await screen.findByTestId('lyrics-panel');
            const synth = screen.getByTestId('synth-panel');

            expect(synth.compareDocumentPosition(lyrics) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
            expect(synth.className).toContain('flex-[3]');
            expect(lyrics.className).toContain('flex-[2]');
        });
    });

    describe('song details beside the cover while a panel is open', () => {
        const info: TrackInfoVM = {
            id: track.id,
            quality: { format: 'FLAC', sampleRate: 96000, bitrate: null, lossless: true, hiRes: true, label: 'Hi-Res · FLAC 96 kHz' },
            moods: ['upbeat'],
            energy: 'High',
            themes: [],
            goodFor: [],
            isInstrumental: false,
        };
        const withLyrics: LyricsVM = { plainLyrics: 'Line one', syncedLyrics: null, isSynced: false, providerName: 'LRClib', sourceUrl: null };

        it('keeps the quality and mood on screen with lyrics and the synth both on', async () => {
            localStorage.setItem('now_playing_lyrics', 'true');
            localStorage.setItem('now_playing_synth', 'true');
            getTrackLyrics.mockResolvedValue(withLyrics);
            getTrackInfo.mockResolvedValue(info);
            renderScreen(player({ currentMedia: { ...track, posterUrl: 'https://example.test/cover.jpg' } }));

            const bar = await screen.findByTestId('now-playing-song-bar');
            expect(bar).toHaveTextContent('Carousel');
            expect(await screen.findByTestId('now-playing-track-info')).toHaveTextContent('Hi-Res · FLAC 96 kHz');
            expect(screen.getByTestId('now-playing-track-info')).toHaveTextContent('Upbeat · High energy');
            expect(bar.querySelector('img')).toHaveAttribute('alt', 'Carousel');
        });

        it('leaves the cover to the ring when the ring style is on', async () => {
            localStorage.setItem('now_playing_synth', 'true');
            localStorage.setItem('now_playing_synth_style', 'ring');
            getTrackLyrics.mockResolvedValue(null);
            renderScreen(player({ currentMedia: { ...track, posterUrl: 'https://example.test/cover.jpg' } }));

            const bar = await screen.findByTestId('now-playing-song-bar');
            expect(bar.querySelector('img')).toBeNull();
            expect(screen.getByTestId('synth-visualizer')).toHaveAttribute('data-synth-style', 'ring');
        });

        it('shows the big cover again with both panels closed', () => {
            getTrackLyrics.mockResolvedValue(null);
            renderScreen(player());

            expect(screen.queryByTestId('now-playing-song-bar')).not.toBeInTheDocument();
        });
    });

    describe('synth style menu', () => {
        it('picks a style, turns the synth on and remembers the choice', async () => {
            getTrackLyrics.mockResolvedValue(null);
            const first = renderScreen(player());

            fireEvent.click(screen.getByRole('button', { name: 'Synth style' }));
            fireEvent.click(within(screen.getByRole('dialog', { name: 'Synth style' })).getByRole('button', { name: /Waves/ }));

            expect(screen.getByTestId('synth-visualizer')).toHaveAttribute('data-synth-style', 'waves');
            expect(localStorage.getItem('now_playing_synth_style')).toBe('waves');
            expect(localStorage.getItem('now_playing_synth')).toBe('true');
            first.unmount();

            renderScreen(player());
            expect(screen.getByTestId('synth-visualizer')).toHaveAttribute('data-synth-style', 'waves');
        });

        it('remembers the colour choice', () => {
            getTrackLyrics.mockResolvedValue(null);
            renderScreen(player());

            fireEvent.click(screen.getByRole('button', { name: 'Synth style' }));
            const menu = screen.getByRole('dialog', { name: 'Synth style' });
            expect(within(menu).getByRole('button', { name: 'From album art' })).toHaveAttribute('aria-pressed', 'true');
            fireEvent.click(within(menu).getByRole('button', { name: 'Theme accent' }));

            expect(localStorage.getItem('now_playing_synth_colors')).toBe('theme');
            expect(within(menu).getByRole('button', { name: 'Theme accent' })).toHaveAttribute('aria-pressed', 'true');
        });

        it('closes on Escape without minimizing the player', () => {
            getTrackLyrics.mockResolvedValue(null);
            const value = player();
            renderScreen(value);

            fireEvent.click(screen.getByRole('button', { name: 'Synth style' }));
            const menu = screen.getByRole('dialog', { name: 'Synth style' });
            fireEvent.keyDown(within(menu).getByRole('button', { name: /Bars/ }), { key: 'Escape' });

            expect(screen.queryByRole('dialog', { name: 'Synth style' })).not.toBeInTheDocument();
            expect(value.setFullscreen).not.toHaveBeenCalled();
            expect(screen.getByRole('button', { name: 'Synth style' })).toHaveFocus();
        });

        it('closes when the audio settings open', () => {
            getTrackLyrics.mockResolvedValue(null);
            renderScreen(player());

            fireEvent.click(screen.getByRole('button', { name: 'Synth style' }));
            fireEvent.click(screen.getByRole('button', { name: 'Audio' }));

            expect(screen.queryByRole('dialog', { name: 'Synth style' })).not.toBeInTheDocument();
        });
    });
});
