import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CollectionsPage from './CollectionsPage';
import { DialogProvider } from '../../../dialogs';
import type { LibrarySummary } from '../../../api/Media/libraryService';
import { StorageKeys } from '../../../utils/storageKeys';
import type { NavItem } from '../../../layouts/MainLayout';
import { SidebarItemsContext, type SidebarItems } from '../../../layouts/sidebarItems';

const mocks = vi.hoisted(() => ({
    getLibraries: vi.fn(),
    getGlobalCollections: vi.fn(),
    getLibraryCollections: vi.fn(),
    createCollection: vi.fn(),
}));

vi.mock('../../../api/Media/libraryService', () => ({
    libraryService: { getLibraries: mocks.getLibraries },
}));

vi.mock('../../../api/Collections/collectionService', () => ({
    collectionService: { getGlobalCollections: mocks.getGlobalCollections, getLibraryCollections: mocks.getLibraryCollections },
}));

vi.mock('../../../api/Collections/collectionAdminService', () => ({
    collectionAdminService: { createCollection: mocks.createCollection },
}));

vi.mock('../../../api/System/pluginAdminService', () => ({
    pluginAdminService: { getChronologyProviders: () => Promise.resolve([]), getCollectionSyncProviders: () => Promise.resolve([]) },
}));

const library = (id: string, name: string, type: string): LibrarySummary => ({ id, name, type, isBeingWatched: false });

const libraries = [
    library('movies', 'Movies', 'Movie'),
    library('test-movies', 'Test Movies', 'Movie'),
    library('shows', 'Shows', 'TvShow'),
    library('music', 'Music', 'Music'),
    library('test-music', 'TestMusic', 'Music'),
];

const libraryEntry = (id: string, title: string, isPinned: boolean): NavItem =>
    ({ id, title, path: `/library/${id}`, type: 'library', isPinned, order: 0 });

const musicEntry = (isPinned: boolean): NavItem =>
    ({ id: 'music', title: 'Music', path: '/music', type: 'system', isPinned, order: 0 });

const sidebar = (items: NavItem[], loaded = true): SidebarItems => ({ items, loaded });

const renderPage = (items: SidebarItems | null, url = '/') => render(
    <DialogProvider>
        <MemoryRouter initialEntries={[url]}>
            {items
                ? <SidebarItemsContext.Provider value={items}><CollectionsPage /></SidebarItemsContext.Provider>
                : <CollectionsPage />}
        </MemoryRouter>
    </DialogProvider>,
);

const tabNames = () => within(screen.getByRole('navigation')).getAllByRole('button').map(button => button.textContent);

const pinnedEverywhere = sidebar([
    libraryEntry('movies', 'Movies', true),
    libraryEntry('test-movies', 'Test Movies', true),
    libraryEntry('shows', 'Shows', true),
    musicEntry(true),
]);

beforeEach(() => {
    localStorage.clear();
    mocks.getLibraries.mockResolvedValue(libraries);
    mocks.getGlobalCollections.mockResolvedValue([]);
    mocks.getLibraryCollections.mockResolvedValue([]);
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
});

describe('CollectionsPage library tabs', () => {
    it('leaves out a library the profile unpinned from the sidebar', async () => {
        renderPage(sidebar([
            libraryEntry('movies', 'Movies', true),
            libraryEntry('test-movies', 'Test Movies', false),
            libraryEntry('shows', 'Shows', true),
            musicEntry(true),
        ]));

        await screen.findByRole('button', { name: 'Movies' });

        expect(tabNames()).toEqual(['Global', 'Movies', 'Shows']);
    });

    it('opens Global instead of an unpinned library named in the link, and never fetches its collections', async () => {
        renderPage(sidebar([libraryEntry('test-movies', 'Test Movies', false), musicEntry(true)]), '/?tab=test-movies');

        await waitFor(() => expect(mocks.getGlobalCollections).toHaveBeenCalled());

        expect(screen.queryByRole('button', { name: 'Test Movies' })).toBeNull();
        expect(mocks.getLibraryCollections).not.toHaveBeenCalled();
    });

    it('waits for the sidebar before fetching a library tab', async () => {
        const items = [libraryEntry('movies', 'Movies', true), musicEntry(true)];
        const view = renderPage(sidebar(items, false), '/?tab=movies');

        await waitFor(() => expect(mocks.getLibraries).toHaveBeenCalled());
        await Promise.resolve();

        expect(mocks.getLibraryCollections).not.toHaveBeenCalled();
        expect(screen.queryByRole('button', { name: 'Movies' })).toBeNull();

        view.rerender(
            <DialogProvider>
                <MemoryRouter initialEntries={['/?tab=movies']}>
                    <SidebarItemsContext.Provider value={sidebar(items)}><CollectionsPage /></SidebarItemsContext.Provider>
                </MemoryRouter>
            </DialogProvider>,
        );

        await waitFor(() => expect(mocks.getLibraryCollections).toHaveBeenCalledWith('movies', undefined, false));
        expect(mocks.getGlobalCollections).not.toHaveBeenCalled();
    });

    it('shows every library but music outside the client layout', async () => {
        renderPage(null);

        await screen.findByRole('button', { name: 'Movies' });

        expect(tabNames()).toEqual(['Global', 'Movies', 'Test Movies', 'Shows']);
    });
});

describe('CollectionsPage music libraries', () => {
    it('gives music libraries no tab while Music is pinned', async () => {
        renderPage(pinnedEverywhere);

        await screen.findByRole('button', { name: 'Movies' });

        expect(tabNames()).toEqual(['Global', 'Movies', 'Test Movies', 'Shows']);
    });

    it.each(['music', 'test-music'])('opens Global from an old link to the %s library, never fetching its collections', async tab => {
        renderPage(pinnedEverywhere, `/?tab=${tab}`);

        await waitFor(() => expect(mocks.getGlobalCollections).toHaveBeenCalled());

        expect(mocks.getLibraryCollections).not.toHaveBeenCalled();
    });
});

describe('CollectionsPage creating a collection', () => {
    it('creates it in the open library tab', async () => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
        mocks.createCollection.mockResolvedValue('new-collection');
        renderPage(pinnedEverywhere, '/?tab=movies');
        await screen.findByRole('button', { name: 'Movies' });

        fireEvent.click(screen.getByRole('button', { name: 'Create collection' }));
        const dialog = await screen.findByRole('dialog');
        fireEvent.change(within(dialog).getAllByRole('textbox')[0], { target: { value: 'Heists' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Create Collection' }));

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({ title: 'Heists', libraryId: 'movies' });
    });
});
