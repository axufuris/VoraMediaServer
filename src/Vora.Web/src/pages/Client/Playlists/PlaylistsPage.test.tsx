import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
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
let playlistLoads = 0;

vi.mock('../../../api/Collections/playlistService', () => ({
    playlistService: {
        getPlaylists: () => {
            playlistLoads += 1;
            return Promise.resolve(playlists);
        },
        getShared: () => Promise.resolve(shared),
        createPlaylist: () => Promise.resolve({ id: 'new-1' }),
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
        playlistLoads = 0;
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


    describe('where a playlist opens', () => {
        function Where() {
            return <div data-testid="where">{useLocation().pathname}</div>;
        }

        const renderMusic = (props: Partial<React.ComponentProps<typeof PlaylistsPage>> = {}) => render(
            <MemoryRouter initialEntries={['/music']}>
                <Routes>
                    <Route path="*" element={<PlaylistsPage embedded lockedType="music" {...props} />} />
                </Routes>
                <Where />
            </MemoryRouter>,
        );

        const createManual = async () => {
            await screen.findByText('Road Trip');
            fireEvent.click(screen.getByRole('button', { name: 'New' }));
            fireEvent.click(screen.getByRole('button', { name: /^Manual/ }));
            fireEvent.change(screen.getByPlaceholderText('e.g. Workout'), { target: { value: 'Workout' } });
            fireEvent.click(screen.getByRole('button', { name: 'Create' }));
        };

        it('opens a new music playlist through the host, not on a page of its own', async () => {
            const onOpenPlaylist = vi.fn();
            renderMusic({ onOpenPlaylist });

            await createManual();

            await waitFor(() => expect(onOpenPlaylist).toHaveBeenCalledWith('Playlist', 'new-1'));
            expect(screen.getByTestId('where')).toHaveTextContent('/music');
        });

        it('opens a new playlist on its own page when there is no host', async () => {
            renderMusic();

            await createManual();

            await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/playlist/new-1'));
        });

        it('opens a playlist, a smart one and a shared one through the host', async () => {
            shared = { manual: [{ id: 'p-theirs', name: 'Sunday Jazz', mediaType: 'Music', itemCount: 5, posterUrls: [], backdropUrls: [], ...theirs }], smart: [] };
            const onOpenPlaylist = vi.fn();
            renderMusic({ onOpenPlaylist });

            fireEvent.click(await screen.findByText('Road Trip'));
            fireEvent.click(screen.getByText('Heavy Rotation'));
            fireEvent.click(screen.getByRole('tab', { name: 'Shared' }));
            fireEvent.click(screen.getByText('Sunday Jazz'));

            expect(onOpenPlaylist.mock.calls).toEqual([
                ['Playlist', 'p-music'],
                ['SmartPlaylist', 's-music'],
                ['Playlist', 'p-theirs'],
            ]);
            expect(screen.getByTestId('where')).toHaveTextContent('/music');
        });

        it('opens a tile on its own page when there is no host', async () => {
            renderMusic();

            fireEvent.click(await screen.findByText('Road Trip'));

            expect(screen.getByTestId('where')).toHaveTextContent('/playlist/p-music');
        });

        it('reads the lists again when the host asks, without a skeleton', async () => {
            const view = renderMusic({ refreshKey: 0 });
            await screen.findByText('Road Trip');
            expect(playlistLoads).toBe(1);

            view.rerender(
                <MemoryRouter initialEntries={['/music']}>
                    <Routes>
                        <Route path="*" element={<PlaylistsPage embedded lockedType="music" refreshKey={1} />} />
                    </Routes>
                    <Where />
                </MemoryRouter>,
            );

            await waitFor(() => expect(playlistLoads).toBe(2));
            expect(screen.getByText('Road Trip')).toBeInTheDocument();
        });
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
