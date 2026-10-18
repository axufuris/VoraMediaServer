import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MediaDetailsPage from './MediaDetailsPage';
import { PlayerContext, type PlayerContextType } from '../../../contexts/usePlayer';
import type { Episode, MediaItem, UpNextResultVM } from '../../../api/Media/mediaService';
import { StorageKeys } from '../../../utils/storageKeys';

const api = vi.hoisted(() => ({
    getMediaItem: vi.fn<(id: string) => Promise<MediaItem>>(),
    getUpNext: vi.fn<(id: string) => Promise<UpNextResultVM>>(),
    startSession: vi.fn<(mediaId: string, deviceId: string, start: number) => Promise<{ sessionId: string; streamUrl: string }>>(),
    alert: vi.fn<(message: string) => Promise<void>>(),
}));

vi.mock('../../../api/Media/mediaService', () => ({
    mediaService: { getMediaItem: (id: string) => api.getMediaItem(id), getUpNext: (id: string) => api.getUpNext(id), setRating: () => Promise.resolve() },
}));
vi.mock('../../../api/Streaming/streamingService', () => ({
    streamingService: { startSession: (mediaId: string, deviceId: string, start: number) => api.startSession(mediaId, deviceId, start) },
}));
vi.mock('../../../api/Watchlist/watchlistService', () => ({ watchlistService: { check: () => Promise.resolve(false) } }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('../../../hooks/useTitleTrackChoices', () => ({
    useTitleTrackChoices: () => ({ videoId: '', audioId: '', subtitleId: 'none', audioPickedHere: false, pickReplaced: false }),
}));
vi.mock('../../../components/Media/TrackChoiceChips', () => ({ default: () => null }));
vi.mock('../../../components/Media/ItemMenu/useMediaItemActions', () => ({ useMediaItemActions: () => ({ entries: [], dialogs: null }) }));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: (message: string) => api.alert(message), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const playMedia = vi.fn();

const player: PlayerContextType = {
    currentMedia: null, isPlaying: false, isMinimized: false, volume: 1, sessionId: null,
    playMedia, playQueue: vi.fn(), addToQueue: vi.fn(), playNext: vi.fn(), nextTrack: vi.fn(), previousTrack: vi.fn(), jumpToQueueIndex: vi.fn(),
    hasNext: false, hasPrevious: false, queue: [], queueIndex: 0, isShuffled: false, toggleShuffle: vi.fn(), repeatMode: 'off', cycleRepeatMode: vi.fn(),
    togglePlayPause: vi.fn(), seek: vi.fn(), skipForward: vi.fn(), skipBackward: vi.fn(), setMinimized: vi.fn(),
    isFullscreen: false, toggleFullscreen: vi.fn(), setFullscreen: vi.fn(), closePlayer: vi.fn(), setVolume: vi.fn(), changeStreams: vi.fn(),
    videoRef: { current: null }, radioSeed: null, radioLabel: null, startRadio: vi.fn(), getAudioAnalyser: () => null,
};

const titles = ['Anjin', 'Servants of Two Masters', 'Tomorrow Is Tomorrow'];
const episode = (n: number, fields: Partial<Episode> = {}): Episode => ({ id: `ep-${n}`, episodeNumber: n, title: titles[n - 1], overview: `Plot of episode ${n}.`, durationMinutes: 60, ...fields });

const seasonOne: MediaItem = {
    id: 'season-1', title: 'Season 1', type: 'Season', seasonNumber: 1, overview: '',
    tvShowId: 'shogun', tvShowTitle: 'Shōgun', tvShowOverview: 'In Japan in the year 1600, at the dawn of a century-defining civil war.',
    libraryId: 'shows', collectionIds: [],
    cast: [{ actorId: 'sanada', tmdbId: 1, name: 'Hiroyuki Sanada', characterName: 'Yoshii Toranaga', order: 0, role: 'Actor' }],
    episodes: [episode(1, { isPlayed: true }), episode(2, { resumePositionSeconds: 1250 }), episode(3)],
};

const servantsOfTwoMasters: MediaItem = {
    id: 'ep-2', title: 'Servants of Two Masters', type: 'Episode', seasonNumber: 1, episodeNumber: 2, resumePositionSeconds: 1250,
    tvShowTitle: 'Shōgun', libraryId: 'shows', collectionIds: [],
};

const show = (seasonCount: number): MediaItem => ({
    id: 'shogun', title: 'Shōgun', type: 'TvShow', overview: 'In Japan in the year 1600.', libraryId: 'shows', collectionIds: [],
    seasons: Array.from({ length: seasonCount }, (_, i) => ({ id: `season-${i + 1}`, seasonNumber: i + 1, title: `Season ${i + 1}`, episodeCount: 3 })),
    cast: seasonOne.cast,
});

const items: Record<string, MediaItem> = { 'season-1': seasonOne, 'ep-2': servantsOfTwoMasters };

function openPage(id: string) {
    render(
        <PlayerContext.Provider value={player}>
            <MemoryRouter initialEntries={[`/media/${id}`]}>
                <Routes>
                    <Route path="/media/:id" element={<MediaDetailsPage />} />
                </Routes>
            </MemoryRouter>
        </PlayerContext.Provider>,
    );
}

beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
    playMedia.mockReset();
    api.alert.mockReset().mockResolvedValue(undefined);
    api.startSession.mockReset().mockResolvedValue({ sessionId: 'session', streamUrl: '/play' });
    api.getUpNext.mockReset().mockResolvedValue({ nextItem: { id: 'ep-2', title: 'Servants of Two Masters', type: 'Episode', seasonNumber: 1, episodeNumber: 2 }, relatedLists: [] });
    api.getMediaItem.mockReset().mockImplementation(id => {
        const item = items[id];
        return item ? Promise.resolve(item) : Promise.reject(new Error(`No ${id}`));
    });
});

describe('a season page', () => {
    it('resumes its episode in progress from the Play button instead of failing', async () => {
        openPage('season-1');

        const resume = await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });
        fireEvent.click(resume);

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-2', 'device-1', 1250));
        expect(playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'ep-2', startPosition: 1250, playbackContextType: 'tvshow' }));
        expect(api.alert).not.toHaveBeenCalled();
    });

    it('starts its next episode over from the start-over button', async () => {
        openPage('season-1');

        fireEvent.click(await screen.findByRole('button', { name: 'Start over' }));

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-2', 'device-1', 0));
    });

    it("tells the season's story with the show's overview when the season has none of its own", async () => {
        openPage('season-1');

        expect(await screen.findByText('In Japan in the year 1600, at the dawn of a century-defining civil war.')).toBeInTheDocument();
        expect(screen.queryByText('No overview available.')).toBeNull();
    });

    it('lists the episodes above the cast', async () => {
        openPage('season-1');

        const episodes = await screen.findByRole('heading', { name: 'Episodes' });
        const cast = screen.getByText('Cast & Crew');

        expect(episodes.compareDocumentPosition(cast) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it("plays an episode from its picture, from where it was left, rather than opening the episode's page", async () => {
        openPage('season-1');

        fireEvent.click(await screen.findByRole('button', { name: 'Resume Servants of Two Masters' }));
        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-2', 'device-1', 1250));

        fireEvent.click(screen.getByRole('button', { name: 'Play Tomorrow Is Tomorrow' }));
        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-3', 'device-1', 0));
        expect(playMedia).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'ep-3', subtitle: 'S1 E3 - Shōgun' }));
        expect(api.getMediaItem).not.toHaveBeenCalledWith('ep-3');
    });

    it('plays a watched episode from the start', async () => {
        openPage('season-1');

        fireEvent.click(await screen.findByRole('button', { name: 'Play Anjin' }));

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-1', 'device-1', 0));
    });
});

describe('a show page', () => {
    it("lists a single season's episodes on the show's own page, with no Seasons row to click through", async () => {
        items.shogun = show(1);
        openPage('shogun');

        expect(await screen.findByRole('heading', { name: 'Episodes' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Play Tomorrow Is Tomorrow' })).toBeInTheDocument();
        expect(screen.queryByText('Seasons')).toBeNull();
        expect(await screen.findByRole('button', { name: 'Resume S1 E2 20:50' })).toBeInTheDocument();
    });

    it('keeps the Seasons row for a show with more than one season', async () => {
        items.shogun = show(2);
        openPage('shogun');

        expect(await screen.findByText('Seasons')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Episodes' })).toBeNull();
    });
});
