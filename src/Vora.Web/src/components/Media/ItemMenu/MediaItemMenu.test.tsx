import { describe, it, expect, vi, beforeAll, afterAll, beforeEach, afterEach } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { MediaItem } from '../../../api/Media/mediaService';
import type { LibraryItem } from '../../../api/Media/libraryService';
import type { WatchlistTarget } from '../../../api/Watchlist/watchlistService';
import { ActionMenuList } from '../../Client/Primitives/ActionMenu';
import MediaCard from '../../Client/Primitives/MediaCard';
import RecommendationRow from '../RecommendationRow';
import { DialogProvider } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';
import MediaItemMenuProvider from './MediaItemMenuProvider';
import MediaMenuArea from './MediaMenuArea';
import { mediaMenuTarget, type MediaMenuTarget } from './mediaItemMenu';
import { useMediaItemActions } from './useMediaItemActions';
import { LONG_PRESS_MS } from './useMediaMenuTrigger';

type WatchlistDetails = { type: string; title: string; posterUrl?: string };

const getMediaItem = vi.fn<(id: string, serverId?: string) => Promise<MediaItem>>();
const markAsPlayed = vi.fn<(id: string, isPlayed: boolean, serverId?: string) => Promise<void>>();
const watchlist = {
    check: vi.fn<(target: WatchlistTarget, serverId?: string) => Promise<boolean>>(),
    toggle: vi.fn<(target: WatchlistTarget, details: WatchlistDetails, serverId?: string) => Promise<boolean>>(),
};
const admin = {
    getThumbnailsLock: vi.fn<(id: string, serverId?: string) => Promise<{ locked: boolean }>>(),
    setThumbnailsLock: vi.fn<(id: string, locked: boolean, serverId?: string) => Promise<{ locked: boolean }>>(),
    refreshItemMetadata: vi.fn<(id: string, force?: boolean, serverId?: string) => Promise<void>>(),
    triggerMediaScan: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
    analyzeMedia: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
    regenerateMediaItemThumbnails: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
    deleteMediaItem: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
    clearMatch: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
    removeMetadata: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
};

vi.mock('../../../api/Media/mediaService', () => ({
    mediaService: {
        getMediaItem: (id: string, serverId?: string) => getMediaItem(id, serverId),
        markAsPlayed: (id: string, isPlayed: boolean, serverId?: string) => markAsPlayed(id, isPlayed, serverId),
    },
}));

vi.mock('../../../api/Watchlist/watchlistService', () => ({
    watchlistService: {
        check: (target: WatchlistTarget, serverId?: string) => watchlist.check(target, serverId),
        toggle: (target: WatchlistTarget, details: WatchlistDetails, serverId?: string) => watchlist.toggle(target, details, serverId),
    },
}));

vi.mock('../../../api/Media/libraryAdminService', () => ({
    libraryAdminService: {
        getThumbnailsLock: (id: string, serverId?: string) => admin.getThumbnailsLock(id, serverId),
        setThumbnailsLock: (id: string, locked: boolean, serverId?: string) => admin.setThumbnailsLock(id, locked, serverId),
        refreshItemMetadata: (id: string, force?: boolean, serverId?: string) => admin.refreshItemMetadata(id, force, serverId),
        triggerMediaScan: (id: string, serverId?: string) => admin.triggerMediaScan(id, serverId),
        analyzeMedia: (id: string, serverId?: string) => admin.analyzeMedia(id, serverId),
        regenerateMediaItemThumbnails: (id: string, serverId?: string) => admin.regenerateMediaItemThumbnails(id, serverId),
        deleteMediaItem: (id: string, serverId?: string) => admin.deleteMediaItem(id, serverId),
        clearMatch: (id: string, serverId?: string) => admin.clearMatch(id, serverId),
        removeMetadata: (id: string, serverId?: string) => admin.removeMetadata(id, serverId),
    },
}));

vi.mock('../EditMetadataModal', () => ({
    default: ({ isOpen, itemId, serverId }: { isOpen: boolean; itemId: string; serverId?: string }) =>
        isOpen ? <div data-testid="edit-metadata">{itemId} on {serverId ?? 'this server'}</div> : null,
}));
vi.mock('../FixMatchModal', () => ({ default: () => <div data-testid="fix-match" /> }));
vi.mock('../MediaInfoDialog', () => ({ default: () => <div data-testid="media-info" /> }));
vi.mock('../../Admin/MarkerEditorModal', () => ({
    default: ({ isOpen }: { isOpen: boolean }) => isOpen ? <div data-testid="marker-editor" /> : null,
}));
vi.mock('../../Collections/AddToCollectionModal', () => ({
    default: ({ isOpen }: { isOpen: boolean }) => isOpen ? <div data-testid="add-to-collection" /> : null,
}));
vi.mock('../../Collections/AddToPlaylistModal', () => ({
    default: ({ isOpen }: { isOpen: boolean }) => isOpen ? <div data-testid="add-to-playlist" /> : null,
}));

class TestPointerEvent extends MouseEvent {
    readonly pointerId: number;
    readonly pointerType: string;

    constructor(type: string, init: PointerEventInit = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 0;
        this.pointerType = init.pointerType ?? '';
    }
}

const title = (overrides: Partial<MediaItem>): MediaItem => ({
    id: 'movie-1',
    title: 'Arrival',
    type: 'Movie',
    libraryId: 'lib-movies',
    collectionIds: [],
    mediaParts: [{ id: 'part-1' }],
    ...overrides,
} as MediaItem);

const movie = title({});
const show = title({ id: 'show-1', title: 'Top Gear', type: 'TvShow', mediaParts: [] });
const longShow = title({
    ...show,
    seasons: [{ id: 'season-1', seasonNumber: 1, episodeCount: 10 }, { id: 'season-2', seasonNumber: 2, episodeCount: 12 }] as MediaItem['seasons'],
});
const season = title({ id: 'season-3', title: 'Season 3', type: 'Season', tvShowId: 'show-1', tvShowTitle: 'Top Gear', seasonNumber: 3, mediaParts: [] });
const episode = title({
    id: 'episode-8', title: 'Episode 8', type: 'Episode', tvShowId: 'show-1', seasonId: 'season-3',
    tvShowTitle: 'Top Gear', seasonNumber: 3, episodeNumber: 8,
});

function Where() {
    const location = useLocation();
    return <div data-testid="where">{location.pathname}</div>;
}

interface PosterProps {
    target?: MediaMenuTarget;
    onOpen?: () => void;
    withHost?: boolean;
}

function renderPoster(props: PosterProps = {}) {
    const { onOpen = vi.fn(), withHost = true } = props;
    const target = 'target' in props ? props.target : mediaMenuTarget(movie, 'srv1');
    const poster = <MediaCard title="Poster" onClick={onOpen} menu={target} />;
    render(
        <MemoryRouter initialEntries={['/']}>
            <DialogProvider>
                <div data-vora-client="">
                    <Routes>
                        <Route path="*" element={withHost ? <MediaItemMenuProvider>{poster}<Where /></MediaItemMenuProvider> : poster} />
                    </Routes>
                </div>
            </DialogProvider>
        </MemoryRouter>,
    );
    return screen.getByRole('button', { name: /Poster/ });
}

const labels = () => within(screen.getByRole('menu')).getAllByRole('menuitem').map(item => item.textContent);

async function openMenuFor(media: MediaItem, target?: MediaMenuTarget) {
    getMediaItem.mockResolvedValue(media);
    const poster = renderPoster({ target: target ?? mediaMenuTarget(media, 'srv1') });
    fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });
    await screen.findAllByRole('menuitem');
    return poster;
}

describe('the menu on a poster', () => {
    beforeAll(() => vi.stubGlobal('PointerEvent', TestPointerEvent));
    afterAll(() => vi.unstubAllGlobals());

    beforeEach(() => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
        admin.getThumbnailsLock.mockResolvedValue({ locked: false });
        admin.setThumbnailsLock.mockResolvedValue({ locked: true });
        admin.refreshItemMetadata.mockResolvedValue();
        admin.triggerMediaScan.mockResolvedValue();
        admin.analyzeMedia.mockResolvedValue();
        admin.regenerateMediaItemThumbnails.mockResolvedValue();
        admin.deleteMediaItem.mockResolvedValue();
        admin.clearMatch.mockResolvedValue();
        markAsPlayed.mockResolvedValue();
        watchlist.check.mockResolvedValue(false);
        watchlist.toggle.mockResolvedValue(true);
    });

    afterEach(() => {
        cleanup();
        vi.clearAllMocks();
        vi.useRealTimers();
        localStorage.clear();
    });

    it('opens on a right-click and offers an admin everything the title page does', async () => {
        await openMenuFor(movie);

        expect(getMediaItem).toHaveBeenCalledWith('movie-1', 'srv1');
        expect(screen.getByRole('menu')).toHaveTextContent('Arrival');
        expect(labels()).toEqual([
            'Mark as watched', 'Add to watchlist',
            'Media info', 'Edit metadata', 'Add to playlist', 'Add to collection',
            'Fix match…', 'Remove metadata…', 'Refresh metadata', 'Scan files', 'Analyze media',
            'Regenerate thumbnails', 'Lock thumbnails', 'Edit markers', 'Delete item',
        ]);
    });

    it('says it is loading until the title arrives', async () => {
        let arrive: (media: MediaItem) => void = () => {};
        getMediaItem.mockReturnValue(new Promise<MediaItem>(resolve => { arrive = resolve; }));
        const poster = renderPoster();

        fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });
        expect(screen.getByRole('menu')).toHaveTextContent('Loading…');

        await act(async () => { arrive(movie); });
        expect(labels()).toContain('Edit metadata');
    });

    it('says so when the title cannot be loaded', async () => {
        getMediaItem.mockRejectedValue(new Error('gone'));
        const poster = renderPoster();

        fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });

        expect(await screen.findByText("Couldn't load this title.")).toBeInTheDocument();
    });

    it('offers someone who is not an admin only what they can do', async () => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'false');

        await openMenuFor(movie);

        expect(labels()).toEqual(['Mark as watched', 'Add to watchlist', 'Media info', 'Add to playlist', 'Add to collection']);
        expect(admin.getThumbnailsLock).not.toHaveBeenCalled();
    });

    it('offers a show a match fix but nothing about files or thumbnails', async () => {
        await openMenuFor(show);

        expect(labels()).toEqual([
            'Mark as watched', 'Add to watchlist',
            'Edit metadata', 'Add to playlist', 'Add to collection',
            'Fix match…', 'Remove metadata…', 'Refresh metadata', 'Scan files', 'Analyze media', 'Delete item',
        ]);
    });

    it('offers a season the way to its show', async () => {
        await openMenuFor(season);

        expect(screen.getByRole('menu')).toHaveTextContent('Top Gear · Season 3');
        expect(labels().slice(0, 4)).toEqual(['Mark as watched', 'Add to watchlist', 'Go to show', 'Edit metadata']);
        expect(labels()).not.toContain('Fix match…');
    });

    it('offers an episode the way to its season and its show', async () => {
        await openMenuFor(episode);

        expect(labels().slice(0, 5)).toEqual(['Mark as watched', 'Add to watchlist', 'Media info', 'Go to season', 'Go to show']);
        fireEvent.click(screen.getByRole('menuitem', { name: 'Go to season' }));

        expect(screen.getByTestId('where')).toHaveTextContent('/server/srv1/media/season-3');
        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });

    it('shows the lock as it is', async () => {
        admin.getThumbnailsLock.mockResolvedValue({ locked: true });

        await openMenuFor(movie);

        expect(await screen.findByRole('menuitem', { name: 'Unlock thumbnails' })).toBeInTheDocument();
    });

    it('closes on Escape and on a click outside', async () => {
        const poster = await openMenuFor(movie);

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
        expect(screen.queryByRole('menu')).not.toBeInTheDocument();

        fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });
        await screen.findAllByRole('menuitem');
        fireEvent.click(screen.getByTestId('action-menu-backdrop'));
        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });

    it('opens the dialog an entry stands for, on the server the title is on', async () => {
        await openMenuFor(movie);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Edit metadata' }));

        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
        expect(screen.getByTestId('edit-metadata')).toHaveTextContent('movie-1 on srv1');
    });

    it('queues a refresh on that server and says so', async () => {
        await openMenuFor(movie);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Refresh metadata' }));

        expect(admin.refreshItemMetadata).toHaveBeenCalledWith('movie-1', true, 'srv1');
        expect(await screen.findByText('Metadata refresh started in the background!')).toBeInTheDocument();
    });

    it('deletes only after being confirmed, then tells the page', async () => {
        const onDeleted = vi.fn();
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onDeleted }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete item' }));
        expect(admin.deleteMediaItem).not.toHaveBeenCalled();
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }));

        await waitFor(() => expect(onDeleted).toHaveBeenCalled());
        expect(admin.deleteMediaItem).toHaveBeenCalledWith('movie-1', 'srv1');
    });

    it('offers to clear a manual match only on a title that was matched by hand', async () => {
        await openMenuFor(title({ lockedFields: ['Title', 'Match'] }));

        expect(labels()).toContain('Clear manual match');
        expect(labels().indexOf('Clear manual match')).toBe(labels().indexOf('Fix match…') + 1);
    });

    it('does not offer it on a title whose match was automatic', async () => {
        await openMenuFor(title({ lockedFields: ['Title'] }));
        expect(labels()).not.toContain('Clear manual match');
    });

    it('does not offer it on an episode', async () => {
        await openMenuFor(title({ ...episode, lockedFields: ['Match'] }));
        expect(labels()).not.toContain('Clear manual match');
    });

    it('clears the match on that server once confirmed, and tells the page', async () => {
        const onChanged = vi.fn();
        const matched = title({ lockedFields: ['Match'] });
        await openMenuFor(matched, mediaMenuTarget(matched, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Clear manual match' }));
        expect(admin.clearMatch).not.toHaveBeenCalled();
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Clear match' }));

        await waitFor(() => expect(admin.clearMatch).toHaveBeenCalledWith('movie-1', 'srv1'));
        expect(await screen.findByText('Manual match cleared. The title is being matched again in the background.')).toBeInTheDocument();
    });

    it('keeps the match when the question is cancelled', async () => {
        await openMenuFor(title({ lockedFields: ['Match'] }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Clear manual match' }));
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(admin.clearMatch).not.toHaveBeenCalled();
    });

    it('removes the metadata of a title on that server once confirmed, and tells the page', async () => {
        const onChanged = vi.fn();
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Remove metadata…' }));
        expect(admin.removeMetadata).not.toHaveBeenCalled();
        const question = await screen.findByRole('dialog');
        expect(question).toHaveTextContent('Details you locked and artwork you uploaded stay.');
        fireEvent.click(within(question).getByRole('button', { name: 'Remove metadata' }));

        await waitFor(() => expect(admin.removeMetadata).toHaveBeenCalledWith('movie-1', 'srv1'));
        expect(await screen.findByText(/^Metadata removed\./)).toBeInTheDocument();
    });

    it('keeps the metadata when that question is cancelled', async () => {
        await openMenuFor(movie);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Remove metadata…' }));
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(admin.removeMetadata).not.toHaveBeenCalled();
    });

    it('offers a title with no metadata a way to match it again instead', async () => {
        await openMenuFor(title({ lockedFields: ['Unmatched'] }));

        expect(labels()).not.toContain('Remove metadata…');
        expect(labels().indexOf('Match automatically')).toBe(labels().indexOf('Fix match…') + 1);
    });

    it('matches a title with no metadata again on that server once confirmed', async () => {
        const unmatched = title({ lockedFields: ['Unmatched'] });
        await openMenuFor(unmatched, mediaMenuTarget(unmatched, 'srv1', {}));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Match automatically' }));
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Match' }));

        await waitFor(() => expect(admin.clearMatch).toHaveBeenCalledWith('movie-1', 'srv1'));
        expect(await screen.findByText('The title is being matched in the background.')).toBeInTheDocument();
    });

    it('leaves the title alone when the delete is cancelled', async () => {
        const onDeleted = vi.fn();
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onDeleted }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete item' }));
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(admin.deleteMediaItem).not.toHaveBeenCalled();
        expect(onDeleted).not.toHaveBeenCalled();
    });

    it('leads with what the title page keeps as buttons, as they stand', async () => {
        watchlist.check.mockResolvedValue(true);

        await openMenuFor(title({ isPlayed: true }));

        expect(await screen.findByRole('menuitem', { name: 'Remove from watchlist' })).toBeInTheDocument();
        expect(labels().slice(0, 3)).toEqual(['Mark as unwatched', 'Remove from watchlist', 'Media info']);
        expect(watchlist.check).toHaveBeenCalledWith({ mediaItemId: 'movie-1' }, 'srv1');
    });

    it('marks a movie watched on its own server, then hands the page the title as it is now', async () => {
        const onChanged = vi.fn();
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onChanged }));
        const watched = title({ isPlayed: true });
        getMediaItem.mockResolvedValue(watched);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as watched' }));

        await waitFor(() => expect(onChanged).toHaveBeenCalledWith(watched));
        expect(markAsPlayed).toHaveBeenCalledWith('movie-1', true, 'srv1');
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('marks a watched movie unwatched', async () => {
        await openMenuFor(title({ isPlayed: true }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as unwatched' }));

        await waitFor(() => expect(markAsPlayed).toHaveBeenCalledWith('movie-1', false, 'srv1'));
    });

    it('says how many episodes a show holds before marking them all', async () => {
        const onChanged = vi.fn();
        await openMenuFor(longShow, mediaMenuTarget(longShow, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as watched' }));
        const question = await screen.findByRole('dialog', { name: 'Mark 22 episodes as watched?' });
        expect(markAsPlayed).not.toHaveBeenCalled();
        fireEvent.click(within(question).getByRole('button', { name: 'Mark watched' }));

        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(markAsPlayed).toHaveBeenCalledWith('show-1', true, 'srv1');
    });

    it('leaves a show as it was when that question is cancelled', async () => {
        const onChanged = vi.fn();
        await openMenuFor(longShow, mediaMenuTarget(longShow, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as watched' }));
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(markAsPlayed).not.toHaveBeenCalled();
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('says so and leaves the page alone when the title cannot be marked', async () => {
        const onChanged = vi.fn();
        markAsPlayed.mockRejectedValue(new Error('offline'));
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as watched' }));

        expect(await screen.findByText('Failed to update play state.')).toBeInTheDocument();
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('adds the title to the watchlist and offers to take it off next time', async () => {
        const onChanged = vi.fn();
        const poster = await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onChanged }));
        watchlist.check.mockResolvedValue(true);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Add to watchlist' }));

        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(watchlist.toggle).toHaveBeenCalledWith({ mediaItemId: 'movie-1' }, { type: 'Movie', title: 'Arrival', posterUrl: undefined }, 'srv1');

        fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });
        expect(await screen.findByRole('menuitem', { name: 'Remove from watchlist' })).toBeInTheDocument();
    });

    it('asks about the watchlist again each time the menu opens', async () => {
        const poster = await openMenuFor(movie);
        expect(labels()).toContain('Add to watchlist');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

        watchlist.check.mockResolvedValue(true);
        getMediaItem.mockResolvedValue({ ...movie });
        fireEvent.contextMenu(poster, { clientX: 120, clientY: 80 });

        expect(await screen.findByRole('menuitem', { name: 'Remove from watchlist' })).toBeInTheDocument();
        expect(watchlist.check).toHaveBeenCalledTimes(2);
    });

    it('says so when the watchlist cannot be changed', async () => {
        const onChanged = vi.fn();
        watchlist.toggle.mockRejectedValue(new Error('offline'));
        await openMenuFor(movie, mediaMenuTarget(movie, 'srv1', { onChanged }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Add to watchlist' }));

        expect(await screen.findByText('Failed to update watchlist.')).toBeInTheDocument();
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('opens over the poster for the keyboard menu key, which reports no position', async () => {
        getMediaItem.mockResolvedValue(movie);
        const poster = renderPoster();

        fireEvent.contextMenu(poster, { clientX: 0, clientY: 0 });

        expect(await screen.findAllByRole('menuitem')).not.toHaveLength(0);
    });

    it('opens after a finger is held on the poster, without opening the title', async () => {
        vi.useFakeTimers();
        getMediaItem.mockResolvedValue(movie);
        const onOpen = vi.fn();
        const poster = renderPoster({ onOpen });

        fireEvent.pointerDown(poster, { pointerType: 'touch', clientX: 40, clientY: 60 });
        act(() => { vi.advanceTimersByTime(LONG_PRESS_MS - 1); });
        expect(screen.queryByRole('menu')).not.toBeInTheDocument();

        act(() => { vi.advanceTimersByTime(1); });
        expect(screen.getByRole('menu')).toBeInTheDocument();

        fireEvent.pointerUp(poster, { pointerType: 'touch' });
        fireEvent.click(poster);
        expect(onOpen).not.toHaveBeenCalled();

        fireEvent.pointerDown(poster, { pointerType: 'touch', clientX: 40, clientY: 60 });
        fireEvent.pointerUp(poster, { pointerType: 'touch' });
        fireEvent.click(poster);
        expect(onOpen).toHaveBeenCalledTimes(1);
    });

    it('does not open when the finger moves, as it does to scroll a row', () => {
        vi.useFakeTimers();
        getMediaItem.mockResolvedValue(movie);
        const poster = renderPoster();

        fireEvent.pointerDown(poster, { pointerType: 'touch', clientX: 40, clientY: 60 });
        fireEvent.pointerMove(poster, { pointerType: 'touch', clientX: 70, clientY: 60 });
        act(() => { vi.advanceTimersByTime(LONG_PRESS_MS * 2); });

        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });

    it('does not open when the finger lifts early, or for a held mouse button', () => {
        vi.useFakeTimers();
        getMediaItem.mockResolvedValue(movie);
        const poster = renderPoster();

        fireEvent.pointerDown(poster, { pointerType: 'touch', clientX: 40, clientY: 60 });
        fireEvent.pointerUp(poster, { pointerType: 'touch' });
        fireEvent.pointerDown(poster, { pointerType: 'mouse', clientX: 40, clientY: 60 });
        act(() => { vi.advanceTimersByTime(LONG_PRESS_MS * 2); });

        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });

    it('leaves the browser menu alone on a tile that is not a library title', () => {
        const poster = renderPoster({ target: mediaMenuTarget({ id: 'track-1', type: 'Track' }) });

        expect(fireEvent.contextMenu(poster)).toBe(true);
        expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });

    it('leaves the browser menu alone where there is no menu host', () => {
        const poster = renderPoster({ withHost: false });

        expect(fireEvent.contextMenu(poster)).toBe(true);
        expect(getMediaItem).not.toHaveBeenCalled();
    });

    it('works on a row that is not a poster tile', async () => {
        getMediaItem.mockResolvedValue(episode);
        render(
            <MemoryRouter>
                <DialogProvider>
                    <MediaItemMenuProvider>
                        <MediaMenuArea target={mediaMenuTarget(episode)} data-testid="episode-row" className="row">Episode 8</MediaMenuArea>
                    </MediaItemMenuProvider>
                </DialogProvider>
            </MemoryRouter>,
        );

        fireEvent.contextMenu(screen.getByTestId('episode-row'), { clientX: 10, clientY: 10 });

        expect(await screen.findByRole('menuitem', { name: 'Go to show' })).toBeInTheDocument();
        expect(screen.getByTestId('episode-row')).toHaveClass('row');
    });
});

describe('the menu on the title page', () => {
    function TitlePageMenu({ media }: { media: MediaItem }) {
        const actions = useMediaItemActions(media, { serverId: 'srv1', onChanged: vi.fn(), onDeleted: vi.fn() });
        return <ActionMenuList entries={actions.entries} onSelected={vi.fn()} />;
    }

    afterEach(() => {
        cleanup();
        vi.clearAllMocks();
        localStorage.clear();
    });

    it('leaves out what the page already has as buttons', () => {
        render(
            <MemoryRouter>
                <DialogProvider>
                    <TitlePageMenu media={movie} />
                </DialogProvider>
            </MemoryRouter>,
        );

        expect(screen.getAllByRole('menuitem').map(item => item.textContent)).toEqual(['Media info', 'Add to playlist', 'Add to collection']);
        expect(watchlist.check).not.toHaveBeenCalled();
    });
});

describe('a recommendation row', () => {
    beforeAll(() => vi.stubGlobal('PointerEvent', TestPointerEvent));
    afterAll(() => vi.unstubAllGlobals());

    afterEach(() => {
        cleanup();
        vi.clearAllMocks();
        localStorage.clear();
    });

    it('shows a show as watched once its menu marked it, without asking for the row again', async () => {
        const recommended = { id: 'show-1', title: 'Top Gear', type: 'TvShow', unplayedItemCount: 22 } as LibraryItem;
        getMediaItem.mockResolvedValue(longShow);
        markAsPlayed.mockResolvedValue();
        watchlist.check.mockResolvedValue(false);
        render(
            <MemoryRouter>
                <DialogProvider>
                    <div data-vora-client="">
                        <MediaItemMenuProvider>
                            <RecommendationRow list={{ title: 'Because you watched', weight: 1, items: [recommended] }} serverId="srv1" />
                        </MediaItemMenuProvider>
                    </div>
                </DialogProvider>
            </MemoryRouter>,
        );
        expect(screen.getByText('22')).toBeInTheDocument();

        fireEvent.contextMenu(screen.getByRole('button', { name: /Top Gear/ }), { clientX: 120, clientY: 80 });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Mark as watched' }));
        getMediaItem.mockResolvedValue({ ...longShow, isPlayed: true, unplayedItemCount: 0 });
        fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Mark watched' }));

        await waitFor(() => expect(screen.queryByText('22')).not.toBeInTheDocument());
        expect(markAsPlayed).toHaveBeenCalledWith('show-1', true, 'srv1');
    });
});

describe('mediaMenuTarget', () => {
    it.each(['Movie', 'TvShow', 'Season', 'Episode'])('gives a %s a menu', type => {
        expect(mediaMenuTarget({ id: 'a', type }, 'srv1')).toEqual({ id: 'a', serverId: 'srv1' });
    });

    it.each(['Track', 'Album', 'Artist', 'Collection', 'Playlist', undefined, null])('gives a %s none', type => {
        expect(mediaMenuTarget({ id: 'a', type })).toBeUndefined();
    });

    it('gives nothing none', () => {
        expect(mediaMenuTarget(null)).toBeUndefined();
        expect(mediaMenuTarget({ id: '', type: 'Movie' })).toBeUndefined();
    });

    it('carries what the page wants to hear about', () => {
        const onChanged = vi.fn();

        expect(mediaMenuTarget({ id: 'a', type: 'Movie' }, undefined, { onChanged })).toEqual({ id: 'a', serverId: undefined, onChanged });
    });
});
