import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import CollectionDetailsPage from './CollectionDetailsPage';
import type { CollectionDetails } from '../../../api/Collections/collectionService';
import { StorageKeys } from '../../../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getCollectionDetails: vi.fn(),
    getLibraries: vi.fn(),
    createModal: vi.fn(),
}));

vi.mock('../../../api/Collections/collectionService', () => ({
    collectionService: { getCollectionDetails: mocks.getCollectionDetails },
}));
vi.mock('../../../api/Collections/collectionAdminService', () => ({ collectionAdminService: {} }));
vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraries: mocks.getLibraries } }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => {} }));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ alert: vi.fn(), confirm: vi.fn(), prompt: vi.fn() }) }));
vi.mock('../../../components/Client/Primitives/CinematicBackdrop', () => ({ default: () => null }));
vi.mock('../../../components/Client/Primitives/MediaCard', () => ({ default: () => null }));
vi.mock('../../../components/Collections/EditCollectionModal', () => ({ default: () => null }));
vi.mock('../../../components/Collections/ReorderCollectionModal', () => ({ default: () => null }));
vi.mock('../../../components/Collections/CreateCollectionModal', () => ({
    default: (props: { template?: CollectionDetails; activeTab: string; libraryType?: string; onClose: () => void }) => {
        mocks.createModal(props);
        return (
            <div data-testid="create-modal">
                <span>{props.template?.title}</span>
                <button type="button" onClick={props.onClose}>Close create</button>
            </div>
        );
    },
}));

const collection = (over: Partial<CollectionDetails> = {}): CollectionDetails => ({
    id: 'c1', title: 'Halloween Movies', description: 'Scary season picks', defaultSortName: 'Alphabetical', isMixedCollection: false,
    itemCount: 0, lockedFields: [], items: [], defaultSort: 'Alphabetical', libraryId: 'movies-1', autoSyncChronology: false,
    systemGenerated: false, syncIntervalDays: 30, mirrorList: false, ...over,
});

const renderPage = () => render(
    <MemoryRouter initialEntries={['/collection/c1']}>
        <Routes>
            <Route path="/collection/:id" element={<CollectionDetailsPage />} />
        </Routes>
    </MemoryRouter>,
);

const cloneButton = () => screen.queryByRole('button', { name: 'Clone Collection' });

describe('CollectionDetailsPage cloning', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getCollectionDetails.mockResolvedValue(collection());
        mocks.getLibraries.mockResolvedValue([
            { id: 'movies-1', name: 'Movies', type: 'Movie', isBeingWatched: false },
            { id: 'shows-1', name: 'TV Shows', type: 'TvShow', isBeingWatched: false },
        ]);
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
    });

    afterEach(() => localStorage.removeItem(StorageKeys.isServerAdmin));

    it('gives an admin a Clone button beside Edit', async () => {
        renderPage();

        await screen.findByText('Halloween Movies');
        expect(cloneButton()).toBeInTheDocument();
        expect(screen.getByTitle('Edit Metadata')).toBeInTheDocument();
    });

    it('gives anyone else no Clone button', async () => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'false');
        renderPage();

        await screen.findByText('Halloween Movies');
        expect(cloneButton()).not.toBeInTheDocument();
    });

    it('asks for the kind first, and opens nothing until it is chosen', async () => {
        renderPage();
        await screen.findByText('Halloween Movies');

        fireEvent.click(cloneButton() as HTMLElement);

        expect(await screen.findByText('What kind of collection will the copy be?')).toBeInTheDocument();
        expect(screen.queryByTestId('create-modal')).not.toBeInTheDocument();
    });

    it('opens the create dialog filled from this collection, in the library that was chosen', async () => {
        renderPage();
        await screen.findByText('Halloween Movies');
        fireEvent.click(cloneButton() as HTMLElement);
        await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());

        fireEvent.click(screen.getByRole('radio', { name: /^Show/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

        expect(await screen.findByTestId('create-modal')).toHaveTextContent('Halloween Movies');
        expect(screen.queryByText('What kind of collection will the copy be?')).not.toBeInTheDocument();
        const props = mocks.createModal.mock.calls.at(-1)?.[0];
        expect(props.activeTab).toBe('shows-1');
        expect(props.libraryType).toBe('TvShow');
        expect(props.template.id).toBe('c1');
    });

    it('opens the create dialog as global when Global is chosen', async () => {
        renderPage();
        await screen.findByText('Halloween Movies');
        fireEvent.click(cloneButton() as HTMLElement);
        await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());

        fireEvent.click(screen.getByRole('radio', { name: /^Global/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

        await screen.findByTestId('create-modal');
        expect(mocks.createModal.mock.calls.at(-1)?.[0].activeTab).toBe('global');
    });

    it('closes the chooser on Cancel without opening the create dialog', async () => {
        renderPage();
        await screen.findByText('Halloween Movies');
        fireEvent.click(cloneButton() as HTMLElement);
        await screen.findByText('What kind of collection will the copy be?');

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(screen.queryByText('What kind of collection will the copy be?')).not.toBeInTheDocument();
        expect(screen.queryByTestId('create-modal')).not.toBeInTheDocument();
    });

    it('can be cloned again after the create dialog is closed', async () => {
        renderPage();
        await screen.findByText('Halloween Movies');
        fireEvent.click(cloneButton() as HTMLElement);
        await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
        fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
        await screen.findByTestId('create-modal');

        fireEvent.click(screen.getByRole('button', { name: 'Close create' }));
        expect(screen.queryByTestId('create-modal')).not.toBeInTheDocument();
        fireEvent.click(cloneButton() as HTMLElement);

        expect(await screen.findByText('What kind of collection will the copy be?')).toBeInTheDocument();
    });
});
