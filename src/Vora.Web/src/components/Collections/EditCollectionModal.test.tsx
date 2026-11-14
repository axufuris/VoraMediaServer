import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import EditCollectionModal from './EditCollectionModal';
import type { CollectionDetails } from '../../api/Collections/collectionService';
import type { PluginOptionVM } from '../../api/System/pluginAdminService';

const mocks = vi.hoisted(() => ({
    getArtworkOptions: vi.fn(),
    uploadArtwork: vi.fn(),
    addArtworkUrl: vi.fn(),
    deleteArtwork: vi.fn(),
    fetchProviderArtwork: vi.fn(),
    updateCollection: vi.fn(),
    deleteCollection: vi.fn(),
    refreshDescription: vi.fn(),
    getChronologyProviders: vi.fn(),
    getCollectionSyncProviders: vi.fn(),
    getArtworkProviders: vi.fn(),
    getVoraPosters: vi.fn(),
    alert: vi.fn(),
}));

const CHRISTMAS_POSTER = '/api/artwork/vora/holidays/christmas.jpg?v=def';
const UPLOADED_POSTER = '/api/artwork/custom/coll_1_poster.jpg';
const TMDB_POSTER = 'https://image.tmdb.org/t/p/original/poster.jpg';

vi.mock('../../api/Collections/collectionAdminService', () => ({
    collectionAdminService: {
        getArtworkOptions: mocks.getArtworkOptions,
        uploadArtwork: mocks.uploadArtwork,
        addArtworkUrl: mocks.addArtworkUrl,
        deleteArtwork: mocks.deleteArtwork,
        fetchProviderArtwork: mocks.fetchProviderArtwork,
        updateCollection: mocks.updateCollection,
        deleteCollection: mocks.deleteCollection,
    },
}));
vi.mock('../../api/Collections/collectionService', () => ({ collectionService: { refreshDescription: mocks.refreshDescription } }));
vi.mock('../../api/Media/voraPosterService', () => ({ getVoraPosters: mocks.getVoraPosters }));
vi.mock('../../api/System/pluginAdminService', () => ({
    pluginAdminService: {
        getChronologyProviders: mocks.getChronologyProviders,
        getCollectionSyncProviders: mocks.getCollectionSyncProviders,
        getArtworkProviders: mocks.getArtworkProviders,
    },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: mocks.alert, confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const artworkProvider = (id: string, name: string): PluginOptionVM => ({
    id, name, providerName: name, externalIdLabel: '', externalIdPlaceholder: '', isAiPlugin: false, supportedLibraryTypes: ['Movie', 'TvShow'],
});

const collection = (overrides: Partial<CollectionDetails> = {}): CollectionDetails => ({
    id: 'collection-1',
    title: 'Christmas Classics',
    posterUrl: UPLOADED_POSTER,
    defaultSortName: 'Release Date',
    isMixedCollection: false,
    itemCount: 3,
    lockedFields: [],
    items: [],
    defaultSort: 'ReleaseDateAsc',
    autoSyncChronology: false,
    systemGenerated: false,
    syncIntervalDays: 30,
    mirrorList: false,
    ...overrides,
});

const renderModal = (details: CollectionDetails) => render(
    <MemoryRouter>
        <EditCollectionModal isOpen onClose={() => {}} onSaved={() => {}} collection={details} />
    </MemoryRouter>,
);

const pill = (name: 'Your artwork' | 'Vora posters') => within(screen.getByRole('group', { name: 'Where the poster comes from' })).getByRole('button', { name });
const providerSelect = async () => (await screen.findByRole('option', { name: 'TMDB Artwork' })).closest('select') as HTMLSelectElement;
const save = () => fireEvent.click(screen.getByRole('button', { name: 'Save Collection' }));

describe('EditCollectionModal artwork', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.alert.mockResolvedValue(undefined);
        mocks.getChronologyProviders.mockResolvedValue([]);
        mocks.getCollectionSyncProviders.mockResolvedValue([]);
        mocks.getArtworkProviders.mockResolvedValue([artworkProvider('fanart_artwork', 'Fanart.tv Artwork'), artworkProvider('tmdb_artwork', 'TMDB Artwork')]);
        mocks.getArtworkOptions.mockResolvedValue([
            { id: 'uploaded', url: UPLOADED_POSTER, type: 'Poster', isUserUploaded: true },
            { id: 'tmdb', url: TMDB_POSTER, type: 'Poster', isUserUploaded: false },
        ]);
        mocks.uploadArtwork.mockResolvedValue('/api/artwork/custom/new.jpg');
        mocks.addArtworkUrl.mockResolvedValue('/api/artwork/custom/new.jpg');
        mocks.fetchProviderArtwork.mockResolvedValue(undefined);
        mocks.updateCollection.mockResolvedValue(undefined);
        mocks.getVoraPosters.mockResolvedValue({
            categories: [{ id: 'holidays', name: 'Holidays' }],
            posters: [{ id: 'christmas', category: 'holidays', title: 'Christmas', aliases: ['christmas'], url: CHRISTMAS_POSTER }],
        });
    });

    it('opens Posters on Your artwork, picks a poster from the options and locks it', async () => {
        renderModal(collection());

        fireEvent.click(screen.getByRole('button', { name: 'Posters' }));

        expect(pill('Your artwork')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByText('Upload Local File')).toBeInTheDocument();
        expect(screen.getByText('Or Add From URL')).toBeInTheDocument();
        expect(screen.getByText('Selected poster:')).toBeInTheDocument();
        await waitFor(() => expect(mocks.getArtworkOptions).toHaveBeenCalledWith('collection-1', undefined));
        expect(await screen.findByText('Manual')).toBeInTheDocument();
        const tmdb = (await screen.findAllByAltText('poster option')).find(img => img.getAttribute('src') === TMDB_POSTER);
        fireEvent.click(tmdb as HTMLElement);
        save();

        await waitFor(() => expect(mocks.updateCollection).toHaveBeenCalled());
        expect(mocks.updateCollection.mock.calls[0][0]).toBe('collection-1');
        expect(mocks.updateCollection.mock.calls[0][1]).toMatchObject({ posterUrl: TMDB_POSTER, lockedFields: ['PosterUrl'] });
    });

    it('uploads straight to the collection and reloads its artwork', async () => {
        renderModal(collection());

        fireEvent.click(screen.getByRole('button', { name: 'Posters' }));
        await waitFor(() => expect(mocks.getArtworkOptions).toHaveBeenCalledTimes(1));
        const file = new File(['poster'], 'tree.jpg', { type: 'image/jpeg' });
        fireEvent.change(document.querySelector('input[type="file"]') as HTMLInputElement, { target: { files: [file] } });

        await waitFor(() => expect(mocks.uploadArtwork).toHaveBeenCalledWith('collection-1', 'Poster', file, undefined));
        await waitFor(() => expect(mocks.getArtworkOptions).toHaveBeenCalledTimes(2));
    });

    it('fetches provider artwork for the collection with the chosen provider', async () => {
        renderModal(collection());

        fireEvent.click(screen.getByRole('button', { name: 'Posters' }));
        const select = await providerSelect();
        expect(select).toHaveValue('fanart_artwork');
        fireEvent.change(select, { target: { value: 'tmdb_artwork' } });
        fireEvent.click(screen.getByRole('button', { name: 'Fetch Artwork' }));

        await waitFor(() => expect(mocks.fetchProviderArtwork).toHaveBeenCalledWith('collection-1', 'tmdb_artwork', undefined));
    });

    it('opens a Vora poster on Vora posters, where clicking it again leaves no poster', async () => {
        renderModal(collection({ posterUrl: CHRISTMAS_POSTER }));

        fireEvent.click(screen.getByRole('button', { name: 'Posters' }));

        expect(pill('Vora posters')).toHaveAttribute('aria-pressed', 'true');
        const christmas = await screen.findByRole('button', { name: 'Christmas poster' });
        expect(christmas).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(christmas);
        expect(christmas).toHaveAttribute('aria-pressed', 'false');
        save();

        await waitFor(() => expect(mocks.updateCollection).toHaveBeenCalled());
        expect(mocks.updateCollection.mock.calls[0][1].posterUrl).toBeUndefined();
        expect(mocks.updateCollection.mock.calls[0][1].lockedFields).toEqual(['PosterUrl']);
    });

    it('adds a backdrop by address without the poster sources', async () => {
        renderModal(collection());

        fireEvent.click(screen.getByRole('button', { name: 'Backdrops' }));

        expect(screen.queryByRole('group', { name: 'Where the poster comes from' })).not.toBeInTheDocument();
        expect(screen.getByText('Selected backdrop:')).toBeInTheDocument();
        fireEvent.change(screen.getByPlaceholderText('https://...'), { target: { value: 'https://example.com/backdrop.jpg' } });
        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        await waitFor(() => expect(mocks.addArtworkUrl).toHaveBeenCalledWith('collection-1', 'Backdrop', 'https://example.com/backdrop.jpg', undefined));
    });
});
