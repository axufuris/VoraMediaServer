import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CreateCollectionModal from './CreateCollectionModal';
import type { PluginOptionVM } from '../../api/System/pluginAdminService';

const mocks = vi.hoisted(() => ({
    createCollection: vi.fn(),
    uploadArtwork: vi.fn(),
    addArtworkUrl: vi.fn(),
    selectArtwork: vi.fn(),
    findProviderArtwork: vi.fn(),
    getChronologyProviders: vi.fn(),
    getCollectionSyncProviders: vi.fn(),
    getArtworkProviders: vi.fn(),
    getVoraPosters: vi.fn(),
    alert: vi.fn(),
    navigate: vi.fn(),
}));

const HALLOWEEN_POSTER = '/api/artwork/vora/holidays/halloween.jpg?v=abc';
const CHRISTMAS_POSTER = '/api/artwork/vora/holidays/christmas.jpg?v=def';
const STAGED_PREVIEW = 'blob:http://localhost/staged-poster';
const TMDB_POSTER = 'https://image.tmdb.org/t/p/original/star-wars.jpg';

vi.mock('../../api/Collections/collectionAdminService', () => ({
    collectionAdminService: {
        createCollection: mocks.createCollection,
        uploadArtwork: mocks.uploadArtwork,
        addArtworkUrl: mocks.addArtworkUrl,
        selectArtwork: mocks.selectArtwork,
        findProviderArtwork: mocks.findProviderArtwork,
    },
}));
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
vi.mock('react-router-dom', async importOriginal => ({
    ...await importOriginal<typeof import('react-router-dom')>(),
    useNavigate: () => mocks.navigate,
}));

const provider = (id: string, name: string, externalIdLabel: string): PluginOptionVM => ({
    id, name, providerName: name, externalIdLabel, externalIdPlaceholder: '', isAiPlugin: true, supportedLibraryTypes: ['Movie', 'TvShow'],
});

const renderModal = () => render(
    <MemoryRouter>
        <CreateCollectionModal isOpen onClose={() => {}} onSaved={() => {}} activeTab="library-1" />
    </MemoryRouter>,
);

const nameIt = (title: string) => fireEvent.change(screen.getByLabelText('Title'), { target: { value: title } });
const openTab = (name: 'Posters' | 'Backdrops') => fireEvent.click(screen.getByRole('button', { name }));
const pill = (name: 'Your artwork' | 'Vora posters') => within(screen.getByRole('group', { name: 'Where the poster comes from' })).getByRole('button', { name });
const fileInput = () => document.querySelector('input[type="file"]') as HTMLInputElement;
const create = () => fireEvent.click(screen.getByRole('button', { name: 'Create Collection' }));

describe('CreateCollectionModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.alert.mockResolvedValue(undefined);
        mocks.getChronologyProviders.mockResolvedValue([]);
        mocks.getCollectionSyncProviders.mockResolvedValue([
            provider('openai_list', 'AI List', 'Franchise or universe'),
            provider('openai_theme', 'AI Theme', 'Theme or occasion'),
        ]);
        mocks.getArtworkProviders.mockResolvedValue([
            provider('fanart_artwork', 'Fanart.tv Artwork', ''),
            provider('tmdb_artwork', 'TMDB Artwork', ''),
        ]);
        mocks.createCollection.mockResolvedValue('new-collection');
        mocks.uploadArtwork.mockResolvedValue('/api/artwork/custom/coll_new_poster.jpg');
        mocks.addArtworkUrl.mockResolvedValue('/api/artwork/custom/coll_new_backdrop.jpg');
        mocks.selectArtwork.mockResolvedValue(undefined);
        mocks.findProviderArtwork.mockResolvedValue([]);
        mocks.getVoraPosters.mockResolvedValue({
            categories: [{ id: 'holidays', name: 'Holidays' }],
            posters: [
                { id: 'halloween', category: 'holidays', title: 'Halloween', aliases: ['halloween'], url: HALLOWEEN_POSTER },
                { id: 'christmas', category: 'holidays', title: 'Christmas', aliases: ['christmas'], url: CHRISTMAS_POSTER },
            ],
        });
        Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => STAGED_PREVIEW) });
        Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
    });

    it('gives a collection the Vora poster its title names, unless another is picked', async () => {
        renderModal();

        nameIt('Christmas Classics');
        expect(await screen.findByText(/Poster: the Vora/)).toHaveTextContent('Poster: the Vora Christmas poster.');
        create();

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({ title: 'Christmas Classics', posterUrl: CHRISTMAS_POSTER });
        expect(mocks.uploadArtwork).not.toHaveBeenCalled();
        expect(mocks.selectArtwork).not.toHaveBeenCalled();
    });

    it('creates the collection with no poster when the suggested one is clicked off', async () => {
        renderModal();

        nameIt('Christmas Classics');
        await screen.findByText(/Poster: the Vora/);
        fireEvent.click(screen.getByRole('button', { name: 'Change' }));
        const christmas = screen.getByRole('button', { name: 'Christmas poster' });
        expect(christmas).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(christmas);
        expect(christmas).toHaveAttribute('aria-pressed', 'false');
        expect(pill('Vora posters')).toHaveAttribute('aria-pressed', 'true');
        create();

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0].posterUrl).toBeUndefined();
    });

    it('shows both poster sources with the suggestion preselected, and Your artwork offers upload, a link and the providers', async () => {
        renderModal();

        nameIt('Christmas Classics');
        await screen.findByText(/Poster: the Vora/);
        openTab('Posters');

        expect(pill('Vora posters')).toHaveAttribute('aria-pressed', 'true');
        expect(pill('Your artwork')).toHaveAttribute('aria-pressed', 'false');
        expect(screen.getByRole('button', { name: 'Christmas poster' })).toHaveAttribute('aria-pressed', 'true');

        fireEvent.click(pill('Your artwork'));

        expect(screen.getByText('Upload Local File')).toBeInTheDocument();
        expect(fileInput()).toBeInTheDocument();
        expect(screen.getByText('Or Add From URL')).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'Fanart.tv Artwork' })).toBeInTheDocument();
        expect(screen.getByLabelText('Artwork provider')).toHaveValue('fanart_artwork');
        expect(screen.getByRole('button', { name: 'Fetch Artwork' })).toBeInTheDocument();
        expect(screen.getByText('Selected poster:')).toBeInTheDocument();
        expect(screen.queryByText(/create the collection first/)).not.toBeInTheDocument();
    });

    it('opens Posters on Your artwork when no Vora poster is picked', async () => {
        renderModal();

        nameIt('Weekend Picks');
        openTab('Posters');

        expect(pill('Your artwork')).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByText('Upload Local File')).toBeInTheDocument();
    });

    it('uploads a staged file after the collection is created and sets it as the poster', async () => {
        renderModal();

        nameIt('Christmas Classics');
        await screen.findByText(/Poster: the Vora/);
        openTab('Posters');
        fireEvent.click(pill('Your artwork'));
        const file = new File(['poster'], 'tree.jpg', { type: 'image/jpeg' });
        fireEvent.change(fileInput(), { target: { files: [file] } });

        expect(await screen.findByText('Manual')).toBeInTheDocument();
        expect(mocks.uploadArtwork).not.toHaveBeenCalled();
        const staged = screen.getAllByAltText('poster option').find(img => img.getAttribute('src') === STAGED_PREVIEW);
        fireEvent.click(staged as HTMLElement);
        create();

        await waitFor(() => expect(mocks.selectArtwork).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({ title: 'Christmas Classics', lockedFields: ['PosterUrl'] });
        expect(mocks.createCollection.mock.calls[0][0].posterUrl).toBeUndefined();
        expect(mocks.uploadArtwork).toHaveBeenCalledWith('new-collection', 'Poster', file, undefined);
        expect(mocks.selectArtwork).toHaveBeenCalledWith('new-collection', 'Poster', '/api/artwork/custom/coll_new_poster.jpg', undefined);
        expect(mocks.createCollection.mock.invocationCallOrder[0]).toBeLessThan(mocks.uploadArtwork.mock.invocationCallOrder[0]);
        expect(mocks.uploadArtwork.mock.invocationCallOrder[0]).toBeLessThan(mocks.selectArtwork.mock.invocationCallOrder[0]);
        expect(mocks.alert).not.toHaveBeenCalled();
        expect(mocks.navigate).toHaveBeenCalledWith('/collection/new-collection');
    });

    it('creates the collection with a poster fetched from a provider by its title', async () => {
        mocks.findProviderArtwork.mockResolvedValue([
            { id: 'a', url: TMDB_POSTER, type: 'Poster', isUserUploaded: false },
            { id: 'b', url: 'https://image.tmdb.org/t/p/original/backdrop.jpg', type: 'Backdrop', isUserUploaded: false },
        ]);
        renderModal();

        nameIt('Star Wars');
        openTab('Posters');
        await screen.findByRole('option', { name: 'TMDB Artwork' });
        fireEvent.change(screen.getByLabelText('Artwork provider'), { target: { value: 'tmdb_artwork' } });
        fireEvent.click(screen.getByRole('button', { name: 'Fetch Artwork' }));

        await waitFor(() => expect(mocks.findProviderArtwork).toHaveBeenCalledWith('tmdb_artwork', 'Star Wars', undefined));
        const fetched = await screen.findByAltText('poster option');
        expect(fetched).toHaveAttribute('src', TMDB_POSTER);
        fireEvent.click(fetched);
        create();

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({ posterUrl: TMDB_POSTER, lockedFields: ['PosterUrl'] });
        expect(mocks.uploadArtwork).not.toHaveBeenCalled();
        expect(mocks.selectArtwork).not.toHaveBeenCalled();
    });

    it('asks for a title before fetching provider artwork', async () => {
        renderModal();

        openTab('Posters');
        await screen.findByRole('option', { name: 'Fanart.tv Artwork' });
        fireEvent.click(screen.getByRole('button', { name: 'Fetch Artwork' }));

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith(expect.objectContaining({ title: 'Add a title first' })));
        expect(mocks.findProviderArtwork).not.toHaveBeenCalled();
    });

    it('saves a backdrop added by address once the collection exists', async () => {
        renderModal();

        nameIt('Weekend Picks');
        openTab('Backdrops');
        expect(screen.queryByRole('group', { name: 'Where the poster comes from' })).not.toBeInTheDocument();
        fireEvent.change(screen.getByPlaceholderText('https://...'), { target: { value: 'https://example.com/backdrop.jpg' } });
        fireEvent.click(screen.getByRole('button', { name: 'Add' }));
        fireEvent.click(await screen.findByAltText('backdrop option'));
        create();

        await waitFor(() => expect(mocks.selectArtwork).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0].backdropUrl).toBeUndefined();
        expect(mocks.addArtworkUrl).toHaveBeenCalledWith('new-collection', 'Backdrop', 'https://example.com/backdrop.jpg', undefined);
        expect(mocks.selectArtwork).toHaveBeenCalledWith('new-collection', 'Backdrop', '/api/artwork/custom/coll_new_backdrop.jpg', undefined);
    });

    it('turns down an address that is not a web image address', async () => {
        renderModal();

        openTab('Backdrops');
        fireEvent.change(screen.getByPlaceholderText('https://...'), { target: { value: 'javascript:alert(1)' } });
        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith(expect.objectContaining({ title: 'Not an image address' })));
        expect(screen.queryByAltText('backdrop option')).not.toBeInTheDocument();
    });

    it('keeps the new collection and says what was saved when a staged upload fails', async () => {
        mocks.uploadArtwork.mockRejectedValue(new Error('Network Error'));
        renderModal();

        nameIt('Christmas Classics');
        await screen.findByText(/Poster: the Vora/);
        openTab('Posters');
        fireEvent.click(pill('Your artwork'));
        fireEvent.change(fileInput(), { target: { files: [new File(['poster'], 'tree.jpg', { type: 'image/jpeg' })] } });
        const staged = (await screen.findAllByAltText('poster option')).find(img => img.getAttribute('src') === STAGED_PREVIEW);
        fireEvent.click(staged as HTMLElement);
        create();

        await waitFor(() => expect(mocks.alert).toHaveBeenCalled());
        expect(mocks.createCollection).toHaveBeenCalledTimes(1);
        expect(mocks.selectArtwork).not.toHaveBeenCalled();
        const message = mocks.alert.mock.calls[0][0];
        expect(message).toMatchObject({ title: 'Some artwork was not saved', tone: 'danger' });
        expect(message.message).toContain('“Christmas Classics” was created with everything else you entered.');
        expect(message.message).toContain('The poster file “tree.jpg” was not saved');
        expect(message.message).toContain('It has no poster for now. You can add it in Edit Collection.');
        await waitFor(() => expect(mocks.navigate).toHaveBeenCalledWith('/collection/new-collection'));
    });

    it('goes back to the suggested Vora poster after picking other artwork', async () => {
        mocks.findProviderArtwork.mockResolvedValue([{ id: 'a', url: TMDB_POSTER, type: 'Poster', isUserUploaded: false }]);
        renderModal();

        nameIt('Christmas Classics');
        await screen.findByText(/Poster: the Vora/);
        openTab('Posters');
        fireEvent.click(pill('Your artwork'));
        fireEvent.click(screen.getByRole('button', { name: 'Fetch Artwork' }));
        const fetched = (await screen.findAllByAltText('poster option')).find(img => img.getAttribute('src') === TMDB_POSTER);
        fireEvent.click(fetched as HTMLElement);

        fireEvent.click(pill('Vora posters'));
        const christmas = screen.getByRole('button', { name: 'Christmas poster' });
        expect(christmas).toHaveAttribute('aria-pressed', 'false');
        expect(within(christmas).getByText('Suggested')).toBeInTheDocument();
        fireEvent.click(christmas);
        expect(christmas).toHaveAttribute('aria-pressed', 'true');
        create();

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0].posterUrl).toBe(CHRISTMAS_POSTER);
    });

    it('creates a theme collection that shows every October', async () => {
        renderModal();

        nameIt('Halloween Movies');
        fireEvent.click(screen.getByLabelText('Repeat every year'));
        fireEvent.change(document.getElementById('create-collection-season-start-month') as HTMLSelectElement, { target: { value: '10' } });
        fireEvent.change(document.getElementById('create-collection-season-start-day') as HTMLSelectElement, { target: { value: '1' } });
        fireEvent.change(document.getElementById('create-collection-season-end-month') as HTMLSelectElement, { target: { value: '10' } });
        fireEvent.change(document.getElementById('create-collection-season-end-day') as HTMLSelectElement, { target: { value: '31' } });

        await screen.findByRole('option', { name: 'AI Theme' });
        fireEvent.change(screen.getByDisplayValue('Manual Management (None)'), { target: { value: 'openai_theme' } });
        expect(screen.getByText('Theme or occasion')).toBeInTheDocument();
        expect(screen.getByText(/titles TMDB tags with the theme go in directly/)).toBeInTheDocument();
        fireEvent.change(screen.getByPlaceholderText('Describe the list'), { target: { value: 'Halloween' } });

        create();

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({
            title: 'Halloween Movies',
            contentSyncProviderId: 'openai_theme',
            contentSyncExternalId: 'Halloween',
            visibleStartDate: '2000-10-01',
            visibleEndDate: '2000-10-31',
            visibleEveryYear: true,
            posterUrl: HALLOWEEN_POSTER,
        });
    });

    it('keeps the franchise advice for the AI List', async () => {
        renderModal();

        await screen.findByRole('option', { name: 'AI List' });
        fireEvent.change(screen.getByDisplayValue('Manual Management (None)'), { target: { value: 'openai_list' } });

        expect(screen.getByText(/Name a specific franchise or shared universe/)).toBeInTheDocument();
        expect(screen.queryByText(/titles TMDB tags with the theme/)).not.toBeInTheDocument();
    });
});
