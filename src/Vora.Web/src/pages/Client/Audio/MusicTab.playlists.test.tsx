import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import MusicTab from './MusicTab';
import { saveMusicSubTab } from './Music/musicSubTab';
import { SessionKeys, StorageKeys } from '../../../utils/storageKeys';
import type { PlaylistDetailsVM, PlaylistSummaryVM } from '../../../api/Collections/playlistService';
import type { SmartPlaylistDetailVM, SmartPlaylistSummaryVM } from '../../../api/Music/smartPlaylistService';

const mocks = vi.hoisted(() => ({
    getPlaylists: vi.fn(),
    getShared: vi.fn(),
    getPlaylist: vi.fn(),
    createPlaylist: vi.fn(),
    deletePlaylist: vi.fn(),
    listSmart: vi.fn(),
    getSmart: vi.fn(),
    getSmartItems: vi.fn(),
    confirm: vi.fn(),
    reloadAiPlaylists: vi.fn(),
}));

vi.mock('../../../api/Music/musicService', () => ({
    musicService: new Proxy({}, { get: (_target, name) => (name === 'then' ? undefined : () => Promise.resolve([])) }),
}));
vi.mock('../../../api/Collections/playlistService', () => ({
    playlistService: {
        getPlaylists: mocks.getPlaylists,
        getShared: mocks.getShared,
        getPlaylist: mocks.getPlaylist,
        createPlaylist: mocks.createPlaylist,
        deletePlaylist: mocks.deletePlaylist,
    },
}));
vi.mock('../../../api/Music/smartPlaylistService', async importOriginal => ({
    ...await importOriginal<typeof import('../../../api/Music/smartPlaylistService')>(),
    smartPlaylistService: { list: mocks.listSmart, get: mocks.getSmart, getItems: mocks.getSmartItems },
}));
vi.mock('../../../api/Media/mediaService', () => ({ mediaService: {} }));
vi.mock('../../../contexts/usePlayer', () => ({
    usePlayer: () => ({ playQueue: vi.fn(), addToQueue: vi.fn(), playNext: vi.fn(), isShuffled: false, toggleShuffle: vi.fn(), startRadio: vi.fn(), playMedia: vi.fn() }),
}));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));
vi.mock('../../../components/Audio/MusicServerSwitcher', () => ({ default: () => null }));
vi.mock('../../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: mocks.confirm, prompt: () => Promise.resolve(null) }),
}));
vi.mock('./Music/useAiPlaylists', () => ({ useAiPlaylists: () => ({ data: null, reload: mocks.reloadAiPlaylists }) }));
vi.mock('./Music/AiPlaylistsSection', () => ({ AiPlaylistActions: () => null, MakePlaylistDialog: () => null }));

const mine = { isShared: false, isOwner: true, ownerName: 'Sam' };

const roadTrip: PlaylistSummaryVM = { id: 'p1', name: 'Road Trip', mediaType: 'Music', itemCount: 0, posterUrls: [], backdropUrls: [], ...mine };
const workout: PlaylistSummaryVM = { id: 'new-1', name: 'Workout', mediaType: 'Music', itemCount: 0, posterUrls: [], backdropUrls: [], ...mine };
const heavyRotation: SmartPlaylistSummaryVM = { id: 's1', name: 'Heavy Rotation', mediaType: 'Music', trackCount: 0, createdAt: '2026-09-01', updatedAt: '2026-09-01', ...mine };

const details = (summary: PlaylistSummaryVM): PlaylistDetailsVM => ({ ...summary, items: [] });

const smartDetails = (summary: SmartPlaylistSummaryVM): SmartPlaylistDetailVM => ({
    id: summary.id, name: summary.name, mediaType: 'Music', definition: { match: 'All', rules: [], groups: [] },
    createdAt: summary.createdAt, updatedAt: summary.updatedAt, ...mine,
});

function Where() {
    return <div data-testid="where">{useLocation().pathname}</div>;
}

const renderMusic = () => render(
    <MemoryRouter initialEntries={['/music']}>
        <Routes>
            <Route path="/music" element={<MusicTab />} />
        </Routes>
        <Where />
    </MemoryRouter>,
);

const breadcrumb = () => screen.getByRole('navigation', { name: 'Music breadcrumb' });

describe('Music tab playlists', () => {
    beforeEach(() => {
        localStorage.clear();
        sessionStorage.clear();
        Object.values(mocks).forEach(m => m.mockReset());
        saveMusicSubTab('playlists');
        mocks.confirm.mockResolvedValue(true);
        mocks.getPlaylists.mockResolvedValue([roadTrip]);
        mocks.getShared.mockResolvedValue({ manual: [], smart: [] });
        mocks.listSmart.mockResolvedValue([heavyRotation]);
        mocks.getPlaylist.mockImplementation((id: string) => Promise.resolve(details(id === 'new-1' ? workout : roadTrip)));
        mocks.getSmart.mockResolvedValue(smartDetails(heavyRotation));
        mocks.getSmartItems.mockResolvedValue({ mediaType: 'Music', tracks: [] });
        mocks.createPlaylist.mockResolvedValue({ id: 'new-1' });
    });

    it('opens a playlist inside the tab, under a Playlists breadcrumb, without leaving the page', async () => {
        renderMusic();

        fireEvent.click(await screen.findByText('Road Trip'));

        expect(await screen.findByRole('heading', { level: 1, name: 'Road Trip' })).toBeInTheDocument();
        await waitFor(() => expect(within(breadcrumb()).getByText('Road Trip')).toBeInTheDocument());
        expect(within(breadcrumb()).getByRole('button', { name: 'Playlists' })).toBeInTheDocument();
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('goes back to the Playlists list from the breadcrumb', async () => {
        renderMusic();
        fireEvent.click(await screen.findByText('Road Trip'));
        await screen.findByRole('heading', { level: 1, name: 'Road Trip' });

        fireEvent.click(within(breadcrumb()).getByRole('button', { name: 'Playlists' }));

        await waitFor(() => expect(screen.queryByRole('heading', { level: 1, name: 'Road Trip' })).toBeNull());
        expect(screen.queryByRole('navigation', { name: 'Music breadcrumb' })).toBeNull();
        expect(screen.getByText('Road Trip')).toBeVisible();
    });

    it('opens a smart playlist inside the tab the same way', async () => {
        renderMusic();

        fireEvent.click(await screen.findByText('Heavy Rotation'));

        expect(await screen.findByRole('heading', { level: 1, name: 'Heavy Rotation' })).toBeInTheDocument();
        await waitFor(() => expect(within(breadcrumb()).getByText('Heavy Rotation')).toBeInTheDocument());
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('opens a new music playlist in the tab as soon as it is made', async () => {
        renderMusic();
        await screen.findByText('Road Trip');

        fireEvent.click(screen.getByRole('button', { name: 'New' }));
        fireEvent.click(screen.getByRole('button', { name: /^Manual/ }));
        fireEvent.change(screen.getByPlaceholderText('e.g. Workout'), { target: { value: 'Workout' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create' }));

        expect(await screen.findByRole('heading', { level: 1, name: 'Workout' })).toBeInTheDocument();
        expect(mocks.createPlaylist).toHaveBeenCalledWith('Workout', '', 'Music', undefined);
        await waitFor(() => expect(within(breadcrumb()).getByText('Workout')).toBeInTheDocument());
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('returns to a Playlists list that has the new playlist after one is made and left', async () => {
        renderMusic();
        await screen.findByText('Road Trip');
        mocks.getPlaylists.mockResolvedValue([roadTrip, workout]);

        fireEvent.click(screen.getByRole('button', { name: 'New' }));
        fireEvent.click(screen.getByRole('button', { name: /^Manual/ }));
        fireEvent.change(screen.getByPlaceholderText('e.g. Workout'), { target: { value: 'Workout' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create' }));
        await screen.findByRole('heading', { level: 1, name: 'Workout' });

        fireEvent.click(within(breadcrumb()).getByRole('button', { name: 'Playlists' }));

        await waitFor(() => expect(screen.getAllByText('Workout').some(el => el.closest('[hidden]') === null)).toBe(true));
        expect(mocks.reloadAiPlaylists).toHaveBeenCalled();
    });

    it('goes back to the list after Delete, without the deleted playlist', async () => {
        renderMusic();
        fireEvent.click(await screen.findByText('Road Trip'));
        const deleteButton = await screen.findByTitle('Delete Playlist');
        mocks.getPlaylists.mockResolvedValue([]);

        fireEvent.click(deleteButton);

        await waitFor(() => expect(mocks.deletePlaylist).toHaveBeenCalledWith('p1', undefined));
        await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Music breadcrumb' })).toBeNull());
        await waitFor(() => expect(screen.queryByText('Road Trip')).toBeNull());
        expect(screen.getByTestId('where')).toHaveTextContent('/music');
    });

    it('restores an open playlist after a reload, under the sub-tab it was opened from', async () => {
        saveMusicSubTab('forYou');
        localStorage.setItem(StorageKeys.profileToken, `h.${btoa(JSON.stringify({ sub: 'profile-1' }))}.s`);
        sessionStorage.setItem(SessionKeys.musicNavState, JSON.stringify({ view: 'playlist', playlistId: 'p1' }));
        sessionStorage.setItem(SessionKeys.musicNavProfile, 'profile-1');
        renderMusic();

        expect(await screen.findByRole('heading', { level: 1, name: 'Road Trip' })).toBeInTheDocument();
        expect(within(breadcrumb()).getByRole('button', { name: 'For You' })).toBeInTheDocument();
    });
});
