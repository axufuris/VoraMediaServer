import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import MediaDetailsPage from './MediaDetailsPage';
import { PlayerContext, type PlayerContextType } from '../../../contexts/usePlayer';
import type { Episode, MediaItem, UpNextResultVM } from '../../../api/Media/mediaService';
import { StorageKeys } from '../../../utils/storageKeys';

const api = vi.hoisted(() => ({
    getMediaItem: vi.fn<(id: string) => Promise<MediaItem>>(),
    getUpNext: vi.fn<(id: string) => Promise<UpNextResultVM>>(),
    markAsPlayed: vi.fn<(id: string, played: boolean) => Promise<void>>(),
    setRating: vi.fn<(id: string, rating: number | null) => Promise<void>>(),
    startSession: vi.fn<(mediaId: string, deviceId: string, start: number) => Promise<{ sessionId: string; streamUrl: string }>>(),
    alert: vi.fn<(message: string) => Promise<void>>(),
}));

vi.mock('../../../api/Media/mediaService', () => ({
    mediaService: {
        getMediaItem: (id: string) => api.getMediaItem(id),
        getUpNext: (id: string) => api.getUpNext(id),
        markAsPlayed: (id: string, played: boolean) => api.markAsPlayed(id, played),
        setRating: (id: string, rating: number | null) => api.setRating(id, rating),
    },
}));
vi.mock('../../../api/Streaming/streamingService', () => ({
    streamingService: { startSession: (mediaId: string, deviceId: string, start: number) => api.startSession(mediaId, deviceId, start) },
}));
vi.mock('../../../api/Watchlist/watchlistService', () => ({ watchlistService: { check: () => Promise.resolve(false) } }));
const prefs = vi.hoisted(() => ({ episodeLayout: 'Row' as 'Row' | 'List' }));
vi.mock('../../../api/Users/profileService', () => ({
    profileService: {
        getMyPlaybackPreferences: () => Promise.resolve({
            autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15, episodeLayout: prefs.episodeLayout,
        }),
    },
}));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('../../../hooks/useTitleTrackChoices', () => ({
    useTitleTrackChoices: () => ({ videoId: '', audioId: '', subtitleId: 'none', audioPickedHere: false, pickReplaced: false }),
}));
vi.mock('../../../components/Media/TrackChoiceChips', () => ({ default: () => null }));
vi.mock('../../../components/Media/ItemMenu/useMediaItemActions', () => ({
    useMediaItemActions: (media: MediaItem | null) => ({
        entries: media ? [{ kind: 'action', key: `edit-${media.id}`, label: `Edit ${media.title}`, onSelect: () => undefined }] : [],
        dialogs: null,
    }),
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: (message: string) => api.alert(message), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const playMedia = vi.fn();
const scrollTo = vi.fn();

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

const season = (number: number, fields: Partial<MediaItem> = {}): MediaItem => ({
    id: `season-${number}`, title: `Season ${number}`, type: 'Season', seasonNumber: number, overview: '',
    tvShowId: 'shogun', tvShowTitle: 'Shōgun', tvShowOverview: 'In Japan in the year 1600, at the dawn of a century-defining civil war.',
    libraryId: 'shows', collectionIds: [],
    cast: [{ actorId: 'sanada', tmdbId: 1, name: 'Hiroyuki Sanada', characterName: 'Yoshii Toranaga', order: 0, role: 'Actor' }],
    episodes: [episode(1, { isPlayed: true }), episode(2, { resumePositionSeconds: 1250 }), episode(3)],
    ...fields,
});

const servantsOfTwoMasters: MediaItem = {
    id: 'ep-2', title: 'Servants of Two Masters', type: 'Episode', seasonId: 'season-1', seasonNumber: 1, episodeNumber: 2, resumePositionSeconds: 1250,
    overview: 'Plot of episode 2.', tvShowTitle: 'Shōgun', libraryId: 'shows', collectionIds: [],
};

const tomorrowIsTomorrow: MediaItem = {
    id: 'ep-3', title: 'Tomorrow Is Tomorrow', type: 'Episode', seasonId: 'season-1', seasonNumber: 1, episodeNumber: 3,
    overview: 'Plot of episode 3.', tvShowTitle: 'Shōgun', libraryId: 'shows', collectionIds: [],
};

const show = (seasonCount: number): MediaItem => ({
    id: 'shogun', title: 'Shōgun', type: 'TvShow', overview: 'In Japan in the year 1600.', libraryId: 'shows', collectionIds: [],
    seasons: Array.from({ length: seasonCount }, (_, i) => ({ id: `season-${i + 1}`, seasonNumber: i + 1, title: `Season ${i + 1}`, episodeCount: 3 })),
    cast: season(1).cast,
});

let items: Record<string, MediaItem> = {};

function Address() {
    const location = useLocation();
    return <output data-testid="address">{location.pathname + location.search}</output>;
}

function openPage(path: string) {
    return render(
        <PlayerContext.Provider value={player}>
            <MemoryRouter initialEntries={[path]}>
                <Routes>
                    <Route path="/media/:id" element={<><MediaDetailsPage /><Address /></>} />
                </Routes>
            </MemoryRouter>
        </PlayerContext.Provider>,
    );
}

const tile = (container: HTMLElement, episodeId: string) => {
    const found = container.querySelector<HTMLElement>(`[data-episode-id="${episodeId}"] [role="button"]`);
    if (!found) throw new Error(`No tile for ${episodeId}`);
    return found;
};

const heroOverview = () => screen.getByTestId('hero-overview');

beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    vi.stubGlobal('scrollTo', scrollTo.mockReset());
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
    prefs.episodeLayout = 'Row';
    playMedia.mockReset();
    items = { 'season-1': season(1), 'season-2': season(2, { episodes: [episode(1)] }), 'ep-2': servantsOfTwoMasters, 'ep-3': tomorrowIsTomorrow, shogun: show(2) };
    api.alert.mockReset().mockResolvedValue(undefined);
    api.markAsPlayed.mockReset().mockResolvedValue(undefined);
    api.setRating.mockReset().mockResolvedValue(undefined);
    api.startSession.mockReset().mockResolvedValue({ sessionId: 'session', streamUrl: '/play' });
    api.getUpNext.mockReset().mockImplementation(id => Promise.resolve(id === 'season-2'
        ? { nextItem: { id: 'ep-1', title: 'Anjin', type: 'Episode', seasonNumber: 2, episodeNumber: 1 }, relatedLists: [] }
        : { nextItem: { id: 'ep-2', title: 'Servants of Two Masters', type: 'Episode', seasonNumber: 1, episodeNumber: 2 }, relatedLists: [] }));
    api.getMediaItem.mockReset().mockImplementation(id => {
        const item = items[id];
        return item ? Promise.resolve(item) : Promise.reject(new Error(`No ${id}`));
    });
});

describe('a season page', () => {
    it('opens on the episode in progress, describes it, and resumes it from Play', async () => {
        const { container } = openPage('/media/season-1');

        const resume = await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });
        expect(within(screen.getByRole('banner')).getByText('Servants of Two Masters')).toBeInTheDocument();
        expect(heroOverview()).toHaveTextContent('Plot of episode 2.');
        expect(tile(container, 'ep-2')).toHaveAttribute('aria-current', 'true');

        fireEvent.click(resume);

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-2', 'device-1', 1250));
        expect(playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'ep-2', startPosition: 1250, playbackContextType: 'tvshow' }));
        expect(api.alert).not.toHaveBeenCalled();
    });

    it('describes the episode picked in the row, and plays that one from Play', async () => {
        const { container } = openPage('/media/season-1');
        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });

        fireEvent.click(tile(container, 'ep-3'));

        expect(await screen.findByRole('button', { name: 'Play S1 E3' })).toBeInTheDocument();
        expect(heroOverview()).toHaveTextContent('Plot of episode 3.');
        expect(tile(container, 'ep-3')).toHaveAttribute('aria-current', 'true');
        expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1?episode=ep-3');

        fireEvent.click(screen.getByRole('button', { name: 'Play S1 E3' }));
        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-3', 'device-1', 0));
    });

    it('moves the selection along the row with the arrow keys', async () => {
        const { container } = openPage('/media/season-1');
        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });

        tile(container, 'ep-2').focus();
        fireEvent.keyDown(tile(container, 'ep-2'), { key: 'ArrowRight' });

        expect(tile(container, 'ep-3')).toHaveFocus();
        expect(await screen.findByRole('button', { name: 'Play S1 E3' })).toBeInTheDocument();
    });

    it("plays an episode straight from the round button on its picture", async () => {
        openPage('/media/season-1');

        fireEvent.click(await screen.findByRole('button', { name: 'Play Tomorrow Is Tomorrow' }));

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-3', 'device-1', 0));
        expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1');
    });

    it('marks and rates the selected episode, and keeps the season for the menu', async () => {
        openPage('/media/season-1');
        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });

        fireEvent.click(screen.getByRole('button', { name: 'Mark as watched' }));
        await waitFor(() => expect(api.markAsPlayed).toHaveBeenCalledWith('ep-2', true));

        fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
        const menu = screen.getByRole('menu');
        expect(within(menu).getByText('S1 E2 · Servants of Two Masters')).toBeInTheDocument();
        expect(within(menu).getByRole('menuitem', { name: 'Edit Servants of Two Masters' })).toBeInTheDocument();
        expect(within(menu).getByRole('menuitem', { name: 'Edit Season 1' })).toBeInTheDocument();
        expect(within(menu).getByRole('menuitem', { name: 'Rate Season 1' })).toBeInTheDocument();
        fireEvent.click(within(menu).getByRole('menuitem', { name: 'Mark Season 1 as watched' }));
        await waitFor(() => expect(api.markAsPlayed).toHaveBeenCalledWith('season-1', true));
    });

    it("falls back to the show's overview when neither the episode nor the season has one", async () => {
        items['ep-2'] = { ...servantsOfTwoMasters, overview: '' };
        items['season-1'] = season(1, { episodes: [episode(1, { isPlayed: true }), episode(2, { resumePositionSeconds: 1250, overview: '' }), episode(3)] });
        openPage('/media/season-1');

        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });
        expect(heroOverview()).toHaveTextContent('In Japan in the year 1600, at the dawn of a century-defining civil war.');
    });

    it('lists the episodes above the cast', async () => {
        openPage('/media/season-1');

        const episodes = await screen.findByText('Episodes');
        const cast = screen.getByText('Cast & Crew');

        expect(episodes.compareDocumentPosition(cast) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it("switches to another season from the pills at the top", async () => {
        openPage('/media/season-1');

        const pills = await screen.findByRole('navigation', { name: 'Seasons' });
        expect(within(pills).getByRole('button', { name: 'Season 1' })).toHaveAttribute('aria-current', 'page');
        fireEvent.click(within(pills).getByRole('button', { name: 'Season 2' }));

        await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('/media/season-2'));
        expect(await screen.findByRole('button', { name: 'Play S2 E1' })).toBeInTheDocument();
    });

    it('shows no pills for a show with one season', async () => {
        items.shogun = show(1);
        openPage('/media/season-1');

        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });
        expect(screen.queryByRole('navigation', { name: 'Seasons' })).toBeNull();
    });
});

describe('an episode link', () => {
    it('opens its season with the episode selected', async () => {
        const { container } = openPage('/media/ep-3');

        await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1?episode=ep-3'));
        expect(await screen.findByRole('button', { name: 'Play S1 E3' })).toBeInTheDocument();
        expect(tile(container, 'ep-3')).toHaveAttribute('aria-current', 'true');
        expect(heroOverview()).toHaveTextContent('Plot of episode 3.');
    });
});

describe('a show page', () => {
    it("lists a single season's episodes on the show's page, and opens one on its season", async () => {
        items.shogun = show(1);
        const { container } = openPage('/media/shogun');

        expect(await screen.findByText('Episodes')).toBeInTheDocument();
        expect(screen.queryByText('Seasons')).toBeNull();
        expect(await screen.findByRole('button', { name: 'Resume S1 E2 20:50' })).toBeInTheDocument();

        fireEvent.click(tile(container, 'ep-3'));

        await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1?episode=ep-3'));
    });

    it('keeps the Seasons row for a show with more than one season', async () => {
        openPage('/media/shogun');

        expect(await screen.findByText('Seasons')).toBeInTheDocument();
        expect(screen.queryByText('Episodes')).toBeNull();
    });
});

describe('a profile that shows episodes as a list', () => {
    beforeEach(() => { prefs.episodeLayout = 'List'; });

    const listed = (container: HTMLElement, episodeId: string) => {
        const found = container.querySelector<HTMLElement>(`[data-episode-id="${episodeId}"]`);
        if (!found) throw new Error(`No listed ${episodeId}`);
        return found;
    };

    it('lists every episode with its plot instead of the row, and highlights the one the page describes', async () => {
        const { container } = openPage('/media/season-1');

        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });
        expect(await screen.findByText('Plot of episode 1.')).toBeInTheDocument();
        expect(screen.getByText('Plot of episode 3.')).toBeInTheDocument();
        expect(listed(container, 'ep-2')).toHaveAttribute('aria-current', 'true');
        expect(listed(container, 'ep-3')).not.toHaveAttribute('aria-current');
        expect(container.querySelector('[data-episode-id] [role="button"]')).toBeNull();
    });

    it('describes an episode picked by its title, and scrolls up to show it', async () => {
        const { container } = openPage('/media/season-1');
        await screen.findByRole('button', { name: 'Resume S1 E2 20:50' });

        fireEvent.click(await screen.findByRole('button', { name: '3. Tomorrow Is Tomorrow' }));

        expect(await screen.findByRole('button', { name: 'Play S1 E3' })).toBeInTheDocument();
        expect(heroOverview()).toHaveTextContent('Plot of episode 3.');
        expect(listed(container, 'ep-3')).toHaveAttribute('aria-current', 'true');
        expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1?episode=ep-3');
        expect(scrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    });

    it('plays an episode from its picture, from where it was left', async () => {
        openPage('/media/season-1');

        fireEvent.click(await screen.findByRole('button', { name: 'Resume Servants of Two Masters' }));

        await waitFor(() => expect(api.startSession).toHaveBeenCalledWith('ep-2', 'device-1', 1250));
        expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1');
    });

    it("opens an episode on its season from a one-season show's page", async () => {
        items.shogun = show(1);
        openPage('/media/shogun');

        fireEvent.click(await screen.findByRole('button', { name: '3. Tomorrow Is Tomorrow' }));

        await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('/media/season-1?episode=ep-3'));
    });
});
