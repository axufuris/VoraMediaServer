import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CollectionDetailsPage from './CollectionDetailsPage';
import type { CollectionDetails } from '../../../api/Collections/collectionService';
import { StorageKeys } from '../../../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getCollectionDetails: vi.fn(),
    getLibraries: vi.fn(),
    syncChronology: vi.fn(),
    confirm: vi.fn(),
    alert: vi.fn(),
    updated: { current: null as ((id: string) => void) | null },
}));

vi.mock('../../../api/Collections/collectionService', () => ({
    collectionService: { getCollectionDetails: mocks.getCollectionDetails },
}));
vi.mock('../../../api/Collections/collectionAdminService', () => ({ collectionAdminService: { syncChronology: mocks.syncChronology } }));
vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraries: mocks.getLibraries } }));
vi.mock('../../../hooks/useSignalREvent', () => ({
    useSignalREvent: (name: string, handler: (id: string) => void) => {
        if (name === 'CollectionUpdated') mocks.updated.current = handler;
    },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ alert: mocks.alert, confirm: mocks.confirm, prompt: vi.fn() }) }));
vi.mock('../../../components/Client/Primitives/CinematicBackdrop', () => ({ default: () => null }));
vi.mock('../../../components/Client/Primitives/MediaCard', () => ({ default: () => null }));
vi.mock('../../../components/Collections/EditCollectionModal', () => ({ default: () => null }));
vi.mock('../../../components/Collections/ReorderCollectionModal', () => ({ default: () => null }));
vi.mock('../../../components/Collections/CreateCollectionModal', () => ({ default: () => null }));

const collection = (): CollectionDetails => ({
    id: 'c1', title: 'Star Wars Universe', description: '', defaultSortName: 'Chronological', isMixedCollection: false,
    itemCount: 0, lockedFields: [], items: [], defaultSort: 'Chronological', libraryId: null, autoSyncChronology: true,
    systemGenerated: false, syncIntervalDays: 30, mirrorList: false, sortProviderId: 'openai_chronology',
});

const renderPage = () => render(
    <MemoryRouter initialEntries={['/collection/c1']}>
        <Routes>
            <Route path="/collection/:id" element={<CollectionDetailsPage />} />
        </Routes>
    </MemoryRouter>,
);

const startSync = async () => {
    renderPage();
    await screen.findByText('Star Wars Universe');
    fireEvent.click(screen.getByRole('button', { name: 'Sync Timeline' }));
};

describe('Sync Timeline', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => { if (typeof m === 'function') m.mockReset(); });
        mocks.updated.current = null;
        mocks.getCollectionDetails.mockResolvedValue(collection());
        mocks.getLibraries.mockResolvedValue([]);
        mocks.confirm.mockResolvedValue(true);
        mocks.syncChronology.mockResolvedValue(undefined);
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
    });

    afterEach(() => localStorage.removeItem(StorageKeys.isServerAdmin));

    it('keeps saying Syncing until the server says the collection changed, not when the request returns', async () => {
        await startSync();

        await waitFor(() => expect(mocks.syncChronology).toHaveBeenCalledWith('c1'));
        expect(await screen.findByText('Syncing...')).toBeInTheDocument();
        expect(mocks.alert).not.toHaveBeenCalled();

        act(() => mocks.updated.current?.('C1'));

        expect(await screen.findByRole('button', { name: 'Sync Timeline' })).toBeEnabled();
        expect(mocks.alert).not.toHaveBeenCalled();
    });

    it('is not ended by another collection changing', async () => {
        await startSync();
        await screen.findByText('Syncing...');

        act(() => mocks.updated.current?.('some-other-collection'));

        expect(screen.getByText('Syncing...')).toBeInTheDocument();
    });

    it('says so and lets the admin try again when the server refuses the request', async () => {
        mocks.syncChronology.mockRejectedValue(new Error('refused'));

        await startSync();

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith(expect.stringContaining('Failed to sync timeline')));
        expect(screen.getByRole('button', { name: 'Sync Timeline' })).toBeEnabled();
    });

    it('asks nothing of the server when the admin declines the confirmation', async () => {
        mocks.confirm.mockResolvedValue(false);

        await startSync();

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.syncChronology).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Sync Timeline' })).toBeEnabled();
    });
});
