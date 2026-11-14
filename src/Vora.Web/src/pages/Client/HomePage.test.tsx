import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import HomePage from './HomePage';
import type { SmartListEntry } from '../../api/Collections/smartListService';
import type { ContinueWatchingItem } from '../../api/Media/syncService';
import type { NavItem } from '../../layouts/MainLayout';
import { SidebarItemsContext, type SidebarItems } from '../../layouts/sidebarItems';
import { StorageKeys } from '../../utils/storageKeys';
import { serverVault } from '../../utils/serverVault';
import { clearApiClientCache } from '../../api/client';

const mocks = vi.hoisted(() => ({
    getActiveLists: vi.fn(),
    getListEntries: vi.fn(),
    getContinueWatching: vi.fn(),
    getHomeLayout: vi.fn(),
    saveHomeLayout: vi.fn(),
}));

vi.mock('../../api/Collections/smartListService', () => ({
    smartListService: { getActiveLists: mocks.getActiveLists, getListEntries: mocks.getListEntries },
}));
vi.mock('../../api/Media/syncService', () => ({
    syncService: { getContinueWatching: mocks.getContinueWatching, hideFromContinueWatching: vi.fn() },
}));
vi.mock('../../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getHomeLayout: mocks.getHomeLayout, saveHomeLayout: mocks.saveHomeLayout },
}));
vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
vi.mock('../../hooks/useFeatureFlags', () => ({ useFeatureFlags: () => ({ forYou: true }) }));
vi.mock('../../dialogs', () => ({ useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true) }) }));
vi.mock('../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: vi.fn() }) }));
vi.mock('./RecommendationsPage', () => ({ default: () => null }));
vi.mock('./Collections/CollectionsPage', () => ({ default: () => null }));
vi.mock('./WatchlistPage', () => ({ default: () => null }));
vi.mock('./Playlists/PlaylistsPage', () => ({ default: () => null }));

class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
}

const library = (id: string, isPinned: boolean): NavItem =>
    ({ id, serverId: 'home', serverName: 'Home', title: id, path: `/server/home/library/${id}`, type: 'library', isPinned, order: 0 });

const sidebar = (items: NavItem[], loaded = true): SidebarItems => ({ items, loaded });

const page = (value: SidebarItems, path = '/server/home') => (
    <MemoryRouter initialEntries={[path]}>
        <SidebarItemsContext.Provider value={value}>
            <Routes>
                <Route path="/server/:serverId" element={<HomePage />} />
            </Routes>
        </SidebarItemsContext.Provider>
    </MemoryRouter>
);

const movie = (id: string, title: string): SmartListEntry =>
    ({ kind: 'Media', id, title, media: { id, title, type: 'Movie', libraryId: 'lib' } });

const resuming = (id: string, title: string): ContinueWatchingItem =>
    ({ id, title, type: 'Movie', resumePositionSeconds: 600, durationSeconds: 6000 });

const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1'));
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
    vi.stubGlobal('ResizeObserver', NoopResizeObserver);
    mocks.getActiveLists.mockResolvedValue([{ id: 'list-1', title: 'Recently Added Movies & Shows', displayOrder: 0, source: 'Library' }]);
    mocks.getHomeLayout.mockResolvedValue(null);
    mocks.getListEntries.mockResolvedValue([movie('m1', 'Heat')]);
    mocks.getContinueWatching.mockResolvedValue([resuming('m2', 'Alien')]);
    mocks.saveHomeLayout.mockResolvedValue(undefined);
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
    localStorage.clear();
    clearApiClientCache();
});

describe('HomePage and the libraries unpinned from the sidebar', () => {
    it('waits for the sidebar, then asks for each row without the unpinned libraries', async () => {
        const items = [library('movies', true), library('test-movies', false), library('test-shows', false)];
        const view = render(page(sidebar(items, false)));

        await waitFor(() => expect(mocks.getActiveLists).toHaveBeenCalled());
        await waitFor(() => expect(mocks.getHomeLayout).toHaveBeenCalled());
        await Promise.resolve();
        expect(mocks.getListEntries).not.toHaveBeenCalled();

        view.rerender(page(sidebar(items)));

        expect(await screen.findByText('Recently Added Movies & Shows')).toBeTruthy();
        expect(mocks.getListEntries).toHaveBeenCalledTimes(1);
        expect(mocks.getListEntries).toHaveBeenCalledWith('list-1', 'home', ['test-movies', 'test-shows']);
    });

    it('shows Continue Watching from every library without waiting for the sidebar', async () => {
        render(page(sidebar([library('test-shows', false)], false)));

        expect(await screen.findByText('Continue Watching')).toBeTruthy();
        expect(screen.getByText('Alien')).toBeTruthy();
        expect(mocks.getContinueWatching).toHaveBeenCalledTimes(1);
        expect(mocks.getContinueWatching).toHaveBeenCalledWith('profile-1', 'home');
        expect(mocks.getListEntries).not.toHaveBeenCalled();
    });

    it('follows a library pinned or unpinned in the sidebar without reloading the page', async () => {
        const view = render(page(sidebar([library('movies', true), library('test-shows', true)])));
        expect(await screen.findByText('Heat')).toBeTruthy();
        expect(await screen.findByText('Alien')).toBeTruthy();
        expect(mocks.getListEntries).toHaveBeenLastCalledWith('list-1', 'home', []);

        mocks.getListEntries.mockResolvedValue([movie('m3', 'Arrival')]);
        view.rerender(page(sidebar([library('movies', true), library('test-shows', false)])));

        expect(await screen.findByText('Arrival')).toBeTruthy();
        expect(mocks.getListEntries).toHaveBeenLastCalledWith('list-1', 'home', ['test-shows']);
        expect(mocks.getActiveLists).toHaveBeenCalledTimes(1);
        expect(mocks.getContinueWatching).toHaveBeenCalledTimes(1);
    });

    it('asks nothing again when the sidebar is only reordered', async () => {
        const items = [library('movies', true), library('test-shows', false)];
        const view = render(page(sidebar(items)));
        expect(await screen.findByText('Heat')).toBeTruthy();

        view.rerender(page(sidebar([...items].reverse().map((item, order) => ({ ...item, order })))));
        await Promise.resolve();

        expect(mocks.getListEntries).toHaveBeenCalledTimes(1);
        expect(mocks.getContinueWatching).toHaveBeenCalledTimes(1);
    });
});

describe('HomePage on a second server', () => {
    const hiddenRow = JSON.stringify([{ listId: 'list-1', isEnabled: false, orderIndex: 0 }]);
    const shownRow = JSON.stringify([{ listId: 'list-1', isEnabled: true, orderIndex: 0 }]);

    beforeEach(() => {
        serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1'), profileId: 'profile-1', isAdmin: false });
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2'), profileId: 'profile-2', isAdmin: false });
        serverVault.setActiveServerId('home');
        localStorage.setItem('client_home_layout_profile-1_device-1', hiddenRow);
        localStorage.setItem('client_home_layout_profile-2_device-1', shownRow);
    });

    it("asks for Continue Watching and the home layout with that server's own profile", async () => {
        render(page(sidebar([]), '/server/second'));

        expect(await screen.findByText('Alien')).toBeTruthy();
        expect(await screen.findByText('Recently Added Movies & Shows')).toBeTruthy();
        expect(mocks.getContinueWatching).toHaveBeenCalledWith('profile-2', 'second');
        expect(mocks.getContinueWatching).not.toHaveBeenCalledWith('profile-1', expect.anything());
        expect(mocks.getHomeLayout).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        expect(mocks.getHomeLayout).not.toHaveBeenCalledWith('profile-1', expect.anything(), expect.anything());
    });

    it("saves the home layout under that server's own profile, leaving the primary layout alone", async () => {
        render(page(sidebar([]), '/server/second'));
        expect(await screen.findByText('Alien')).toBeTruthy();

        fireEvent.click(screen.getByRole('button', { name: 'Customize' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Save Layout' }));

        await waitFor(() => expect(mocks.saveHomeLayout).toHaveBeenCalledTimes(1));
        expect(mocks.saveHomeLayout).toHaveBeenCalledWith('profile-2', 'device-1', expect.any(String), 'second');
        expect(localStorage.getItem('client_home_layout_profile-2_device-1')).toBe(mocks.saveHomeLayout.mock.calls[0][2]);
        expect(localStorage.getItem('client_home_layout_profile-1_device-1')).toBe(hiddenRow);
    });

    it('keeps using the primary profile on the primary server', async () => {
        render(page(sidebar([])));

        expect(await screen.findByText('Alien')).toBeTruthy();
        expect(mocks.getContinueWatching).toHaveBeenCalledWith('profile-1', 'home');
        expect(mocks.getHomeLayout).toHaveBeenCalledWith('profile-1', 'device-1', 'home');
        expect(screen.queryByText('Recently Added Movies & Shows')).toBeNull();
    });
});
