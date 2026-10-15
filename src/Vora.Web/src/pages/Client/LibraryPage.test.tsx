import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import LibraryPage from './LibraryPage';
import { DialogProvider } from '../../dialogs';
import type { LibraryItem, MediaLibrary } from '../../api/Media/libraryService';

const mocks = vi.hoisted(() => ({
    getLibraryById: vi.fn(),
    getLibraryMedia: vi.fn(),
    getLibraryCollections: vi.fn(),
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
});
