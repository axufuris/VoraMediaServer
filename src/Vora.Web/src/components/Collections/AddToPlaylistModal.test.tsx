import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AddToPlaylistModal from './AddToPlaylistModal';
import type { PlaylistSummaryVM } from '../../api/Collections/playlistService';

const mocks = vi.hoisted(() => ({
    getPlaylists: vi.fn(),
    getPlaylistsContainingItem: vi.fn(),
    createPlaylist: vi.fn(),
    addToPlaylist: vi.fn(),
    removeMediaFromPlaylist: vi.fn(),
}));

vi.mock('../../api/Collections/playlistService', () => ({ playlistService: mocks }));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const summary = (id: string, name: string, mediaType: PlaylistSummaryVM['mediaType']): PlaylistSummaryVM => ({
    id, name, mediaType, itemCount: 3, posterUrls: [], backdropUrls: [], isShared: false, isOwner: true, ownerName: 'Andy',
});

const renderModal = () => render(
    <MemoryRouter>
        <AddToPlaylistModal isOpen onClose={() => {}} mediaId="t1" kind="music" />
    </MemoryRouter>,
);

describe('AddToPlaylistModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getPlaylists.mockResolvedValue([
            summary('m', 'Road Trip', 'Music'),
            summary('x', 'Party Night', 'Mixed'),
            summary('f', 'Comfort Movies', 'Movies'),
        ]);
        mocks.getPlaylistsContainingItem.mockResolvedValue([]);
        mocks.createPlaylist.mockResolvedValue({ id: 'new' });
        mocks.addToPlaylist.mockResolvedValue(undefined);
    });

    // A song can't go in a film playlist, so it isn't offered.
    it('offers a song only the playlists that can hold it', async () => {
        renderModal();

        expect(await screen.findByText('Road Trip')).toBeInTheDocument();
        expect(screen.getByText('Party Night')).toBeInTheDocument();
        expect(screen.queryByText('Comfort Movies')).toBeNull();
    });

    it('creates a music playlist with the song already in it', async () => {
        renderModal();
        await screen.findByText('Road Trip');

        fireEvent.change(screen.getByRole('textbox', { name: 'New playlist name' }), { target: { value: 'Summer' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create' }));

        await waitFor(() => expect(mocks.addToPlaylist).toHaveBeenCalledWith('new', 't1', undefined));
        expect(mocks.createPlaylist).toHaveBeenCalledWith('Summer', undefined, 'Music', undefined);
        expect(await screen.findByText('Summer')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Summer/ })).toHaveAttribute('aria-pressed', 'true');
    });
});
