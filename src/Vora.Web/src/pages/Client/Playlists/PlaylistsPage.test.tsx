import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlaylistsPage from './PlaylistsPage';
import type { PlaylistSummaryVM, SharedPlaylistsVM } from '../../../api/Collections/playlistService';
import type { SmartPlaylistSummaryVM } from '../../../api/Music/smartPlaylistService';
import type { GeneratedMixSummaryVM } from '../../../api/Music/musicService';

const mine = { isShared: false, isOwner: true, ownerName: 'Sam' };

const playlists: PlaylistSummaryVM[] = [
    { id: 'p-music', name: 'Road Trip', mediaType: 'Music', itemCount: 12, posterUrls: [], backdropUrls: [], ...mine, isShared: true },
    { id: 'p-movies', name: 'Comfort Movies', mediaType: 'Movies', itemCount: 4, posterUrls: [], backdropUrls: [], ...mine },
    { id: 'p-mixed', name: 'Party Night', mediaType: 'Mixed', itemCount: 9, posterUrls: [], backdropUrls: [], ...mine },
];

const smart: SmartPlaylistSummaryVM[] = [
    { id: 's-music', name: 'Heavy Rotation', mediaType: 'Music', trackCount: 40, createdAt: '2026-09-01', updatedAt: '2026-09-01', ...mine },
    { id: 's-shows', name: 'Unwatched Comedy', mediaType: 'Shows', trackCount: 8, createdAt: '2026-09-01', updatedAt: '2026-09-01', ...mine },
];

const mixes: GeneratedMixSummaryVM[] = [
    { id: 'm1', slot: 1, name: 'Sunday Chill', trackCount: 50, kind: 'DailyMix', generatedAt: '2026-09-15' },
];

const theirs = { isShared: true, isOwner: false, ownerName: 'Andy' };

let shared: SharedPlaylistsVM = { manual: [], smart: [] };

vi.mock('../../../api/Collections/playlistService', () => ({
    playlistService: {
        getPlaylists: () => Promise.resolve(playlists),
        getShared: () => Promise.resolve(shared),
    },
}));
vi.mock('../../../api/Music/smartPlaylistService', () => ({
    smartPlaylistService: { list: () => Promise.resolve(smart) },
}));
vi.mock('../../../api/Music/musicService', () => ({
    musicService: { getMixes: () => Promise.resolve(mixes) },
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const renderPage = (embedded = false) => render(
    <MemoryRouter>
        <PlaylistsPage embedded={embedded} />
    </MemoryRouter>,
);

describe('PlaylistsPage', () => {
    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        shared = { manual: [], smart: [] };
    });

    it('locked to music shows only music playlists, with no type filter', async () => {
        render(
            <MemoryRouter>
                <PlaylistsPage embedded lockedType="music" />
            </MemoryRouter>,
        );

        expect(await screen.findByText('Road Trip')).toBeInTheDocument();
        expect(screen.getByText('Heavy Rotation')).toBeInTheDocument();
        expect(screen.queryByText('Comfort Movies')).toBeNull();
        expect(screen.queryByText('Unwatched Comedy')).toBeNull();
        expect(screen.queryByText('Sunday Chill')).toBeNull();
        expect(screen.queryByRole('group', { name: 'Media type' })).toBeNull();
        expect(screen.getByRole('tab', { name: 'Yours' })).toHaveAttribute('aria-selected', 'true');
    });

    it('opens on your own Movies & Shows, which also carries mixed-type playlists', async () => {
        renderPage();

        expect(await screen.findByText('Comfort Movies')).toBeInTheDocument();
        expect(screen.getByText('Unwatched Comedy')).toBeInTheDocument();
        expect(screen.getByText('Party Night')).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: 'Yours' })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByRole('button', { name: 'Movies & Shows' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.queryByText('Road Trip')).toBeNull();
    });

    it('opens on Movies & Shows even when an earlier visit picked Music', async () => {
        localStorage.setItem('playlists_active_tab', 'music');
        renderPage(true);

        expect(await screen.findByText('Comfort Movies')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Movies & Shows' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('shows music playlists under Music, without mixes, and keeps the pick for the session', async () => {
        const { unmount } = renderPage(true);
        await screen.findByText('Comfort Movies');

        fireEvent.click(screen.getByRole('button', { name: 'Music' }));

        expect(screen.getByText('Road Trip')).toBeInTheDocument();
        expect(screen.queryByText('Sunday Chill')).toBeNull();
        expect(screen.queryByText('For You')).toBeNull();
        expect(screen.queryByText('Comfort Movies')).toBeNull();
        unmount();

        renderPage(true);
        expect(await screen.findByRole('button', { name: 'Music' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('puts the type filter on the left of New, outside the Yours / Shared tabs', async () => {
        renderPage(true);
        await screen.findByText('Comfort Movies');

        const filter = screen.getByRole('group', { name: 'Media type' });
        const newButton = screen.getByRole('button', { name: 'New' });

        expect(filter.parentElement).toBe(newButton.parentElement?.parentElement);
        expect(filter.compareDocumentPosition(newButton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(within(screen.getByRole('tablist')).queryByRole('button', { name: 'Music' })).toBeNull();
    });

    it('marks a playlist of your own that you have shared', async () => {
        render(
            <MemoryRouter>
                <PlaylistsPage embedded lockedType="music" />
            </MemoryRouter>,
        );

        expect(await screen.findByText(/12 items · Music · Shared/)).toBeInTheDocument();
    });

    it('lists what others have shared, and says whose each one is', async () => {
        shared = {
            manual: [{ id: 'x-1', name: 'Andy’s Road Trip', mediaType: 'Music', itemCount: 20, posterUrls: [], backdropUrls: [], ...theirs }],
            smart: [{ id: 'x-2', name: 'Andy’s Most Played', mediaType: 'Music', trackCount: 30, createdAt: '2026-09-01', updatedAt: '2026-09-01', ...theirs }],
        };
        render(
            <MemoryRouter>
                <PlaylistsPage embedded lockedType="music" />
            </MemoryRouter>,
        );
        await screen.findByText('Road Trip');

        fireEvent.click(screen.getByRole('tab', { name: 'Shared' }));

        expect(screen.getByText('Andy’s Road Trip')).toBeInTheDocument();
        expect(screen.getByText('Andy’s Most Played')).toBeInTheDocument();
        expect(screen.getAllByText('by Andy')).toHaveLength(2);
        // Nothing here is the viewer's to delete.
        expect(screen.queryByRole('button', { name: /delete/i })).toBeNull();
        // Your own playlists are not on this tab.
        expect(screen.queryByText('Road Trip')).toBeNull();
    });

    it('says so when nothing has been shared', async () => {
        render(
            <MemoryRouter>
                <PlaylistsPage embedded lockedType="music" />
            </MemoryRouter>,
        );
        await screen.findByText('Road Trip');

        fireEvent.click(screen.getByRole('tab', { name: 'Shared' }));

        expect(screen.getByText('Nothing shared yet')).toBeInTheDocument();
    });
});
