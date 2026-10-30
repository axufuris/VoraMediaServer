import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CreateCollectionModal from './CreateCollectionModal';
import type { PluginOptionVM } from '../../api/System/pluginAdminService';

const mocks = vi.hoisted(() => ({
    createCollection: vi.fn(),
    getChronologyProviders: vi.fn(),
    getCollectionSyncProviders: vi.fn(),
    getVoraPosters: vi.fn(),
}));

const HALLOWEEN_POSTER = '/api/artwork/vora/holidays/halloween.jpg?v=abc';
const CHRISTMAS_POSTER = '/api/artwork/vora/holidays/christmas.jpg?v=def';

vi.mock('../../api/Collections/collectionAdminService', () => ({ collectionAdminService: { createCollection: mocks.createCollection } }));
vi.mock('../../api/Media/voraPosterService', () => ({ getVoraPosters: mocks.getVoraPosters }));
vi.mock('../../api/System/pluginAdminService', () => ({
    pluginAdminService: { getChronologyProviders: mocks.getChronologyProviders, getCollectionSyncProviders: mocks.getCollectionSyncProviders },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const provider = (id: string, name: string, externalIdLabel: string): PluginOptionVM => ({
    id, name, providerName: name, externalIdLabel, externalIdPlaceholder: '', isAiPlugin: true, supportedLibraryTypes: ['Movie', 'TvShow'],
});

const renderModal = () => render(
    <MemoryRouter>
        <CreateCollectionModal isOpen onClose={() => {}} onSaved={() => {}} activeTab="library-1" />
    </MemoryRouter>,
);

describe('CreateCollectionModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getChronologyProviders.mockResolvedValue([]);
        mocks.getCollectionSyncProviders.mockResolvedValue([
            provider('openai_list', 'AI List', 'Franchise or universe'),
            provider('openai_theme', 'AI Theme', 'Theme or occasion'),
        ]);
        mocks.createCollection.mockResolvedValue('new-collection');
        mocks.getVoraPosters.mockResolvedValue({
            categories: [{ id: 'holidays', name: 'Holidays' }],
            posters: [
                { id: 'halloween', category: 'holidays', title: 'Halloween', aliases: ['halloween'], url: HALLOWEEN_POSTER },
                { id: 'christmas', category: 'holidays', title: 'Christmas', aliases: ['christmas'], url: CHRISTMAS_POSTER },
            ],
        });
    });

    it('gives a collection the Vora poster its title names, unless another is picked', async () => {
        renderModal();

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Christmas Classics' } });
        expect(await screen.findByText(/Poster: the Vora/)).toHaveTextContent('Poster: the Vora Christmas poster.');
        fireEvent.click(screen.getByRole('button', { name: 'Create Collection' }));

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0]).toMatchObject({ title: 'Christmas Classics', posterUrl: CHRISTMAS_POSTER });
    });

    it('creates the collection with no poster when the suggested one is clicked off', async () => {
        renderModal();

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Christmas Classics' } });
        await screen.findByText(/Poster: the Vora/);
        fireEvent.click(screen.getByRole('button', { name: 'Change' }));
        const christmas = screen.getByRole('button', { name: 'Christmas poster' });
        expect(christmas).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(christmas);
        expect(christmas).toHaveAttribute('aria-pressed', 'false');
        fireEvent.click(screen.getByRole('button', { name: 'Create Collection' }));

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0].posterUrl).toBeUndefined();
    });

    it('uses a pasted image address instead of the suggestion', async () => {
        renderModal();

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Christmas Classics' } });
        await screen.findByText(/Poster: the Vora/);
        fireEvent.click(screen.getByRole('button', { name: 'Posters' }));
        fireEvent.change(screen.getByLabelText('Or paste an image URL'), { target: { value: 'https://example.com/poster.jpg' } });
        expect(screen.getByRole('button', { name: 'Christmas poster' })).toHaveAttribute('aria-pressed', 'false');
        fireEvent.click(screen.getByRole('button', { name: 'Create Collection' }));

        await waitFor(() => expect(mocks.createCollection).toHaveBeenCalled());
        expect(mocks.createCollection.mock.calls[0][0].posterUrl).toBe('https://example.com/poster.jpg');
    });

    it('creates a theme collection that shows every October', async () => {
        renderModal();

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Halloween Movies' } });
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

        fireEvent.click(screen.getByRole('button', { name: 'Create Collection' }));

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
