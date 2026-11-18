import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import PlaylistDetailsPage, { type EmbeddedPlaylistHost } from './PlaylistDetailsPage';
import SmartPlaylistDetailsPage, { type EmbeddedSmartPlaylistHost } from './SmartPlaylistDetailsPage';
import type { PlaylistDetailsVM } from '../../../api/Collections/playlistService';
import type { SmartPlaylistDetailVM, SmartPlaylistItemsVM } from '../../../api/Music/smartPlaylistService';

const mocks = vi.hoisted(() => ({
    getPlaylist: vi.fn(),
    deletePlaylist: vi.fn(),
    copyPlaylist: vi.fn(),
    getSmart: vi.fn(),
    getSmartItems: vi.fn(),
    removeSmart: vi.fn(),
    copySmart: vi.fn(),
    confirm: vi.fn(),
}));

vi.mock('../../../api/Collections/playlistService', () => ({
    playlistService: { getPlaylist: mocks.getPlaylist, deletePlaylist: mocks.deletePlaylist, copy: mocks.copyPlaylist },
}));
vi.mock('../../../api/Music/smartPlaylistService', async importOriginal => ({
    ...await importOriginal<typeof import('../../../api/Music/smartPlaylistService')>(),
    smartPlaylistService: { get: mocks.getSmart, getItems: mocks.getSmartItems, remove: mocks.removeSmart, copy: mocks.copySmart },
}));
vi.mock('../../../contexts/usePlayer', () => ({
    usePlayer: () => ({ playMedia: vi.fn(), playQueue: vi.fn(), isShuffled: false, toggleShuffle: vi.fn(), playNext: vi.fn(), addToQueue: vi.fn() }),
}));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: mocks.confirm, prompt: () => Promise.resolve(null) }),
}));

const playlist = (overrides: Partial<PlaylistDetailsVM> = {}): PlaylistDetailsVM => ({
    id: 'p1', name: 'Road Trip', mediaType: 'Music', itemCount: 0, posterUrls: [], backdropUrls: [],
    isShared: false, isOwner: true, ownerName: 'Sam', items: [], ...overrides,
});

const smartPlaylist = (overrides: Partial<SmartPlaylistDetailVM> = {}): SmartPlaylistDetailVM => ({
    id: 's1', name: 'Heavy Rotation', mediaType: 'Music', definition: { match: 'All', rules: [], groups: [] },
    createdAt: '2026-09-01', updatedAt: '2026-09-01', isShared: false, isOwner: true, ownerName: 'Sam', ...overrides,
});

const noItems: SmartPlaylistItemsVM = { mediaType: 'Music', tracks: [] };

function Where() {
    return <div data-testid="where">{useLocation().pathname}</div>;
}

const host = (overrides: Partial<EmbeddedPlaylistHost> = {}): EmbeddedPlaylistHost => ({
    playlistId: 'p1', savedCopy: false, onLoaded: vi.fn(), onDeleted: vi.fn(), onOpenCopy: vi.fn(), onMissing: vi.fn(), ...overrides,
});

const renderPlaylist = (h: EmbeddedPlaylistHost) => render(
    <MemoryRouter initialEntries={['/music']}>
        <PlaylistDetailsPage host={h} />
        <Where />
    </MemoryRouter>,
);

const renderSmart = (h: EmbeddedSmartPlaylistHost) => render(
    <MemoryRouter initialEntries={['/music']}>
        <SmartPlaylistDetailsPage host={h} />
        <Where />
    </MemoryRouter>,
);

describe('a playlist opened inside the Music tab', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.confirm.mockResolvedValue(true);
        mocks.getPlaylist.mockResolvedValue(playlist());
        mocks.getSmart.mockResolvedValue(smartPlaylist());
        mocks.getSmartItems.mockResolvedValue(noItems);
    });

    it('is the host\'s playlist, and tells the host its name for the breadcrumb', async () => {
        const h = host({ playlistId: 'p9' });
        mocks.getPlaylist.mockResolvedValue(playlist({ id: 'p9', name: 'Workout' }));

        renderPlaylist(h);

        expect(await screen.findByRole('heading', { level: 1, name: 'Workout' })).toBeInTheDocument();
        expect(mocks.getPlaylist).toHaveBeenCalledWith('p9', undefined);
        await waitFor(() => expect(h.onLoaded).toHaveBeenCalledWith('Workout'));
    });

    it('goes back to the host after Delete instead of to the playlists page', async () => {
        const h = host();
        renderPlaylist(h);

        fireEvent.click(await screen.findByTitle('Delete Playlist'));

        await waitFor(() => expect(h.onDeleted).toHaveBeenCalledTimes(1));
        expect(mocks.deletePlaylist).toHaveBeenCalledWith('p1', undefined);
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('opens a saved copy through the host, not on a page of its own', async () => {
        const h = host();
        mocks.getPlaylist.mockResolvedValue(playlist({ isOwner: false, ownerName: 'Andy', isShared: true }));
        mocks.copyPlaylist.mockResolvedValue({ id: 'copy-1' });
        renderPlaylist(h);

        fireEvent.click(await screen.findByRole('button', { name: 'Save a copy' }));

        await waitFor(() => expect(h.onOpenCopy).toHaveBeenCalledWith('copy-1'));
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('confirms a saved copy with the banner the host asks for', async () => {
        renderPlaylist(host({ savedCopy: true }));

        expect(await screen.findByText(/Saved to your playlists/)).toBeInTheDocument();
    });

    it('shows no banner unless the host says it is a saved copy', async () => {
        renderPlaylist(host());

        await screen.findByRole('heading', { level: 1, name: 'Road Trip' });
        expect(screen.queryByText(/Saved to your playlists/)).toBeNull();
    });

    it('hands a playlist that is gone back to the host', async () => {
        const h = host();
        mocks.getPlaylist.mockRejectedValue(Object.assign(new Error('gone'), { isAxiosError: true, response: { status: 404 } }));
        renderPlaylist(h);

        fireEvent.click(await screen.findByRole('button', { name: 'Back to playlists' }));

        expect(h.onMissing).toHaveBeenCalledTimes(1);
    });

    it('gives the Edit and Delete buttons the pointer cursor', async () => {
        renderPlaylist(host());

        expect(await screen.findByTitle('Edit Playlist')).toHaveClass('cursor-pointer');
        expect(screen.getByTitle('Delete Playlist')).toHaveClass('cursor-pointer');
    });

    it('opens a smart playlist the same way, and Delete goes back to the host', async () => {
        const h: EmbeddedSmartPlaylistHost = { playlistId: 's1', savedCopy: false, onLoaded: vi.fn(), onDeleted: vi.fn(), onOpenCopy: vi.fn(), onMissing: vi.fn() };
        renderSmart(h);

        expect(await screen.findByRole('heading', { level: 1, name: 'Heavy Rotation' })).toBeInTheDocument();
        await waitFor(() => expect(h.onLoaded).toHaveBeenCalledWith('Heavy Rotation'));

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        await waitFor(() => expect(h.onDeleted).toHaveBeenCalledTimes(1));
        expect(mocks.removeSmart).toHaveBeenCalledWith('s1', undefined);
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });
});

describe('a playlist opened on its own page', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.confirm.mockResolvedValue(true);
        mocks.getPlaylist.mockResolvedValue(playlist());
    });

    it('still returns to the playlists page after Delete', async () => {
        render(
            <MemoryRouter initialEntries={['/playlist/p1']}>
                <Routes>
                    <Route path="/playlist/:id" element={<PlaylistDetailsPage />} />
                    <Route path="/playlists" element={<div>The playlists page</div>} />
                </Routes>
                <Where />
            </MemoryRouter>,
        );

        fireEvent.click(await screen.findByTitle('Delete Playlist'));

        await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/playlists'));
    });
});
