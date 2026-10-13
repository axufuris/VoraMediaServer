import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ManageLibrary from './ManageLibrary';
import type { MediaLibrary } from '../../../api/Media/libraryService';

const mocks = vi.hoisted(() => ({
    getLibraryById: vi.fn(),
    refreshMetadata: vi.fn(),
    updateLibrary: vi.fn(),
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
        updateLibrary: mocks.updateLibrary,
        getLibraryMarkerCoverage: () => new Promise(() => {}),
        getLibraryThumbnailCoverage: () => new Promise(() => {}),
    },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));
vi.mock('../../../components/Admin/FolderBrowser/FolderBrowserModal', () => ({
    default: ({ isOpen, onSelect, onClose }: { isOpen: boolean; onSelect: (path: string) => void; onClose: () => void }) =>
        isOpen ? <button type="button" onClick={() => { onSelect('/media/tv'); onClose(); }}>Select /media/tv</button> : null,
}));

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
    localMetadataMode: 0,
    preferLocalArtwork: false,
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

describe('ManageLibrary local files', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.updateLibrary.mockResolvedValue(undefined);
    });

    it('shows a film library both choices as they are saved', async () => {
        mocks.getLibraryById.mockResolvedValue({ ...library('Movie'), localMetadataMode: 2, preferLocalArtwork: true });
        render(
            <MemoryRouter initialEntries={['/admin/libraries/lib1/manage']}>
                <Routes>
                    <Route path="/admin/libraries/:id/manage" element={<ManageLibrary />} />
                </Routes>
            </MemoryRouter>,
        );
        await screen.findByRole('button', { name: 'Refresh metadata' });

        expect(screen.getByLabelText('Read NFO files')).toHaveValue('2');
        expect(screen.getByLabelText('Prefer local artwork')).toBeChecked();
    });

    it('saves what the admin chose', async () => {
        await renderManage();

        fireEvent.change(screen.getByLabelText('Read NFO files'), { target: { value: '1' } });
        fireEvent.click(screen.getByLabelText('Prefer local artwork'));
        fireEvent.click(screen.getAllByRole('button', { name: 'Save changes' })[0]);

        await waitFor(() => expect(mocks.updateLibrary).toHaveBeenCalledWith(
            'lib1',
            expect.objectContaining({ localMetadataMode: 1, preferLocalArtwork: true }),
            undefined,
        ));
    });

    it('does not offer them to a music library', async () => {
        await renderManage('Music');

        expect(screen.queryByLabelText('Read NFO files')).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Prefer local artwork')).not.toBeInTheDocument();
    });
});

describe('ManageLibrary folders', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
    });

    it('adds a folder picked in the browser straight to the library', async () => {
        await renderManage();

        fireEvent.click(screen.getByRole('button', { name: 'Browse' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select /media/tv' }));

        expect(await screen.findByText('/media/tv')).toBeInTheDocument();
        expect(screen.getByPlaceholderText('Pick or paste a folder (e.g. /media/movies)')).toHaveValue('');
    });

    it('does not add a folder the library already has', async () => {
        mocks.getLibraryById.mockResolvedValue({ ...library('Movie'), folderPaths: ['/media/tv'] });
        render(
            <MemoryRouter initialEntries={['/admin/libraries/lib1/manage']}>
                <Routes>
                    <Route path="/admin/libraries/:id/manage" element={<ManageLibrary />} />
                </Routes>
            </MemoryRouter>,
        );
        await screen.findByRole('button', { name: 'Refresh metadata' });

        fireEvent.click(screen.getByRole('button', { name: 'Browse' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select /media/tv' }));

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith(expect.objectContaining({ title: 'Duplicate path' })));
        expect(screen.getAllByText('/media/tv')).toHaveLength(1);
    });
});
