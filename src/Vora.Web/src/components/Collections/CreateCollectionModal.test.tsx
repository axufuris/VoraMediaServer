import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CreateCollectionModal from './CreateCollectionModal';
import type { PluginOptionVM } from '../../api/System/pluginAdminService';

const mocks = vi.hoisted(() => ({
    createCollection: vi.fn(),
    getChronologyProviders: vi.fn(),
    getCollectionSyncProviders: vi.fn(),
}));

vi.mock('../../api/Collections/collectionAdminService', () => ({ collectionAdminService: { createCollection: mocks.createCollection } }));
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
