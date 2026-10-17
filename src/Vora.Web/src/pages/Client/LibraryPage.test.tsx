import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import LibraryPage from './LibraryPage';
import { DialogProvider } from '../../dialogs';
import type { LibraryItem, MediaLibrary } from '../../api/Media/libraryService';
import { StorageKeys } from '../../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getLibraryById: vi.fn(),
    getLibraryMedia: vi.fn(),
    getLibraryCollections: vi.fn(),
    getMyLibrarySort: vi.fn(),
}));

vi.mock('../../api/Users/profileService', () => ({
    profileService: { getMyLibrarySort: mocks.getMyLibrarySort },
}));

vi.mock('../../api/Media/libraryService', () => ({
    libraryService: { getLibraryById: mocks.getLibraryById, getLibraryMedia: mocks.getLibraryMedia },
}));

vi.mock('../../api/Collections/collectionService', () => ({
    collectionService: { getLibraryCollections: mocks.getLibraryCollections },
}));

vi.mock('../../hooks/useSignalREvent', () => ({ useSignalREvent: () => undefined }));

const libraryId = 'lib-1';

const library = (type: string): MediaLibrary => ({
    id: libraryId,
    name: 'Family',
    type,
    folderPaths: [],
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
    minimumCollectionSize: 3,
    isBeingWatched: false,
    enableRealTimeWatching: false,
    metadataProviderId: 'local_metadata',
    localMetadataMode: 0,
    preferLocalArtwork: false,
});

const video = (id: string, title: string, releaseDate: string): LibraryItem => ({ id, title, releaseDate, type: 'Movie', libraryId });

const renderPage = () => render(
    <DialogProvider>
        <MemoryRouter initialEntries={[`/library/${libraryId}`]}>
            <Routes>
                <Route path="/library/:id" element={<LibraryPage />} />
            </Routes>
        </MemoryRouter>
    </DialogProvider>,
);

const shownTitles = (titles: string[]) => screen.getAllByText(new RegExp(`^(${titles.join('|')})$`)).map(node => node.textContent);

const videos = [
    video('1', 'Beach Day', '2019-07-04'),
    video('2', 'Wedding', '2021-06-12'),
    video('3', 'Birthday', '2020-03-01'),
];

beforeEach(() => {
    localStorage.clear();
    mocks.getLibraryMedia.mockResolvedValue(videos);
    mocks.getLibraryCollections.mockResolvedValue([]);
    mocks.getMyLibrarySort.mockResolvedValue({ sort: null, descending: false });
});

describe('LibraryPage sort', () => {
    it('opens a home video library on its dates, newest first', async () => {
        mocks.getLibraryById.mockResolvedValue(library('HomeVideo'));

        renderPage();

        expect(await screen.findByRole('button', { name: /^Date/ })).toBeInTheDocument();
        expect(shownTitles(['Beach Day', 'Wedding', 'Birthday'])).toEqual(['Wedding', 'Birthday', 'Beach Day']);
    });

    it('keeps a sort the viewer already chose', async () => {
        localStorage.setItem(`vora_library_sort_${libraryId}`, 'title');
        localStorage.setItem(`vora_library_sortdir_${libraryId}`, 'asc');
        mocks.getLibraryById.mockResolvedValue(library('HomeVideo'));

        renderPage();

        expect(await screen.findByRole('button', { name: /^Title/ })).toBeInTheDocument();
        expect(shownTitles(['Beach Day', 'Wedding', 'Birthday'])).toEqual(['Beach Day', 'Birthday', 'Wedding']);
    });

    it('opens other libraries on their titles', async () => {
        mocks.getLibraryById.mockResolvedValue(library('Movie'));

        renderPage();

        const sort = await screen.findByRole('button', { name: /^Title/ });
        expect(within(sort).queryByText('Date')).toBeNull();
    });

    it('opens on the sort chosen for the profile in Settings', async () => {
        mocks.getMyLibrarySort.mockResolvedValue({ sort: 'ReleaseDate', descending: true });
        mocks.getLibraryById.mockResolvedValue(library('Movie'));

        renderPage();

        expect(await screen.findByRole('button', { name: /^Release Date/ })).toBeInTheDocument();
        expect(shownTitles(['Beach Day', 'Wedding', 'Birthday'])).toEqual(['Wedding', 'Birthday', 'Beach Day']);
    });

    it('remembers a sort picked on the page until the profile sort changes', async () => {
        mocks.getLibraryById.mockResolvedValue(library('Movie'));
        const first = renderPage();

        fireEvent.click(await screen.findByRole('button', { name: /^Title/ }));
        fireEvent.click(screen.getByRole('button', { name: /^Release Date/ }));
        first.unmount();

        renderPage();
        expect(await screen.findByRole('button', { name: /^Release Date/ })).toBeInTheDocument();
        cleanup();

        mocks.getMyLibrarySort.mockResolvedValue({ sort: 'DateAdded', descending: true });
        renderPage();
        expect(await screen.findByRole('button', { name: /^Date Added/ })).toBeInTheDocument();
    });

    it('opens on the server default when the profile sort cannot be read', async () => {
        mocks.getMyLibrarySort.mockRejectedValue(new Error('offline'));
        mocks.getLibraryById.mockResolvedValue(library('HomeVideo'));

        renderPage();

        expect(await screen.findByRole('button', { name: /^Date/ })).toBeInTheDocument();
    });
});

describe('LibraryPage posters', () => {
    it('gives an admin no delete button on a poster, since the poster menu has Delete', async () => {
        localStorage.setItem(StorageKeys.isServerAdmin, 'true');
        mocks.getLibraryById.mockResolvedValue(library('Movie'));

        renderPage();

        expect(await screen.findByText('Wedding')).toBeInTheDocument();
        expect(screen.queryByTitle('Delete')).toBeNull();
        expect(screen.queryByRole('button', { name: /^Delete / })).toBeNull();
    });
});
