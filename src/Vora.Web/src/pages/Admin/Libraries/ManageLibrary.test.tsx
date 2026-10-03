import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ManageLibrary from './ManageLibrary';
import type { MediaLibrary } from '../../../api/Media/libraryService';

const mocks = vi.hoisted(() => ({
    getLibraryById: vi.fn(),
    refreshMetadata: vi.fn(),
    confirm: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraryById: mocks.getLibraryById } }));
vi.mock('../../../api/System/pluginAdminService', () => ({
    pluginAdminService: {
        getMetadataProviders: () => Promise.resolve([]),
        getRatingsProviders: () => Promise.resolve([]),
        getArtworkProviders: () => Promise.resolve([]),
    },
}));
vi.mock('../../../api/Media/libraryAdminService', () => ({
    libraryAdminService: {
        refreshMetadata: mocks.refreshMetadata,
        getLibraryMarkerCoverage: () => new Promise(() => {}),
        getLibraryThumbnailCoverage: () => new Promise(() => {}),
    },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));

const library = (type: string): MediaLibrary => ({
    id: 'lib1',
    name: type === 'Music' ? 'Music' : 'Movies',
    type,
    folderPaths: ['/media'],
    scanner: '',
    findExtras: false,
    onlyShowTrailers: false,
    enableVideoPreviewThumbnails: false,
    enableCreditsDetection: false,
    enablePreviewDetection: false,
    episodeSorting: 0,
    episodeOrder: 0,
    useSeasonTitles: false,
    seasonsDisplay: 0,
    enableIntroDetection: false,
    minimumCollectionSize: 2,
    isBeingWatched: false,
    enableRealTimeWatching: false,
    metadataProviderId: 'tmdb_metadata',
});

const renderManage = async (type = 'Movie') => {
    mocks.getLibraryById.mockResolvedValue(library(type));
    render(
        <MemoryRouter initialEntries={['/admin/libraries/lib1/manage']}>
            <Routes>
                <Route path="/admin/libraries/:id/manage" element={<ManageLibrary />} />
            </Routes>
        </MemoryRouter>,
    );
    await screen.findByRole('button', { name: 'Refresh metadata' });
};

describe('ManageLibrary metadata buttons', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.refreshMetadata.mockResolvedValue(undefined);
    });

    it('Refresh metadata only fills in what is missing', async () => {
        await renderManage();

        fireEvent.click(screen.getByRole('button', { name: 'Refresh metadata' }));

        await waitFor(() => expect(mocks.refreshMetadata).toHaveBeenCalledWith('lib1', false, undefined));
        expect(mocks.confirm).not.toHaveBeenCalled();
    });

    it('Replace all metadata asks first, then re-fetches everything', async () => {
        mocks.confirm.mockResolvedValue(true);
        await renderManage();

        fireEvent.click(screen.getByRole('button', { name: 'Replace all metadata' }));

        await waitFor(() => expect(mocks.refreshMetadata).toHaveBeenCalledWith('lib1', true, undefined));
        expect(mocks.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Replace all metadata?' }));
    });

    it('Replace all metadata does nothing when the admin backs out', async () => {
        mocks.confirm.mockResolvedValue(false);
        await renderManage();

        fireEvent.click(screen.getByRole('button', { name: 'Replace all metadata' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.refreshMetadata).not.toHaveBeenCalled();
    });

    it('on a music library, says replacing covers artist and album artwork', async () => {
        mocks.confirm.mockResolvedValue(false);
        await renderManage('Music');

        fireEvent.click(screen.getByRole('button', { name: 'Replace all metadata' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalledWith(expect.objectContaining({
            message: expect.stringContaining('every artist and album'),
        })));
    });
});
