import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CloneCollectionDialog from './CloneCollectionDialog';
import type { CollectionDetails } from '../../api/Collections/collectionService';
import type { LibrarySummary } from '../../api/Media/libraryService';

const mocks = vi.hoisted(() => ({
    getLibraries: vi.fn(),
    onChoose: vi.fn(),
    onClose: vi.fn(),
}));

vi.mock('../../api/Media/libraryService', () => ({ libraryService: { getLibraries: mocks.getLibraries } }));

const library = (id: string, name: string, type: string): LibrarySummary => ({ id, name, type, isBeingWatched: false });

const MOVIES = library('movies-1', 'Movies', 'Movie');
const MOVIES_4K = library('movies-2', '4K Movies', 'Movie');
const SHOWS = library('shows-1', 'TV Shows', 'TvShow');
const MUSIC = library('music-1', 'Music', 'Music');

const source = (libraryId?: string): CollectionDetails => ({
    id: 'source', title: 'Halloween Movies', defaultSortName: '', isMixedCollection: false, itemCount: 3, lockedFields: [],
    items: [], defaultSort: 'ReleaseDateAsc', autoSyncChronology: false, systemGenerated: false, syncIntervalDays: 30, mirrorList: false,
    libraryId,
});

const renderDialog = (collection: CollectionDetails) => render(
    <MemoryRouter>
        <CloneCollectionDialog onClose={mocks.onClose} collection={collection} onChoose={mocks.onChoose} />
    </MemoryRouter>,
);

const radio = (name: string) => screen.getByRole('radio', { name: new RegExp(`^${name}`) }) as HTMLInputElement;
const proceed = () => fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

describe('CloneCollectionDialog', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getLibraries.mockResolvedValue([MOVIES, SHOWS, MUSIC]);
    });

    it('asks whether the copy is a Movie, Show or Global collection', async () => {
        renderDialog(source('movies-1'));

        await waitFor(() => expect(radio('Movie').checked).toBe(true));
        expect(screen.getByText('What kind of collection will the copy be?')).toBeInTheDocument();
        expect(screen.getAllByRole('radio').map(r => (r as HTMLInputElement).value)).toEqual(['Movies', 'Shows', 'Global']);
    });

    it('starts on the kind the original is, so Continue clones it as it is', async () => {
        renderDialog(source('shows-1'));

        await waitFor(() => expect(radio('Show').checked).toBe(true));
        proceed();

        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'shows-1', libraryType: 'TvShow' });
    });

    it('starts on Global for a global collection', async () => {
        renderDialog(source(undefined));

        await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
        expect(radio('Global').checked).toBe(true);
        proceed();

        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'global' });
    });

    it('moves a movie collection to the shows library when Show is picked', async () => {
        renderDialog(source('movies-1'));
        await waitFor(() => expect(radio('Movie').checked).toBe(true));

        fireEvent.click(radio('Show'));
        proceed();

        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'shows-1', libraryType: 'TvShow' });
    });

    it('makes a library collection global when Global is picked', async () => {
        renderDialog(source('movies-1'));
        await waitFor(() => expect(radio('Movie').checked).toBe(true));

        fireEvent.click(radio('Global'));
        proceed();

        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'global' });
    });

    it('asks which library only when there is more than one of the kind', async () => {
        mocks.getLibraries.mockResolvedValue([MOVIES, MOVIES_4K, SHOWS]);
        renderDialog(source('movies-2'));

        const select = await screen.findByLabelText('Library') as HTMLSelectElement;
        expect(select.value).toBe('movies-2');
        expect(Array.from(select.options).map(o => o.textContent)).toEqual(['Movies', '4K Movies']);

        fireEvent.change(select, { target: { value: 'movies-1' } });
        proceed();
        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'movies-1', libraryType: 'Movie' });

        fireEvent.click(radio('Show'));
        expect(screen.queryByLabelText('Library')).not.toBeInTheDocument();
    });

    it('offers the first library of the kind when the original lives elsewhere', async () => {
        mocks.getLibraries.mockResolvedValue([MOVIES, MOVIES_4K, SHOWS]);
        renderDialog(source('shows-1'));
        await waitFor(() => expect(radio('Show').checked).toBe(true));

        fireEvent.click(radio('Movie'));

        expect((screen.getByLabelText('Library') as HTMLSelectElement).value).toBe('movies-1');
    });

    it('disables a kind the server has no library for, and says so', async () => {
        mocks.getLibraries.mockResolvedValue([MOVIES, MUSIC]);
        renderDialog(source('movies-1'));

        await waitFor(() => expect(radio('Movie').checked).toBe(true));
        expect(radio('Show').disabled).toBe(true);
        expect(screen.getByText('You have no TV show library.')).toBeInTheDocument();
        expect(radio('Global').disabled).toBe(false);
    });

    it('never offers a music library', async () => {
        mocks.getLibraries.mockResolvedValue([MUSIC]);
        renderDialog(source(undefined));

        await waitFor(() => expect(radio('Movie').disabled).toBe(true));
        expect(radio('Global').checked).toBe(true);
        expect(radio('Show').disabled).toBe(true);
    });

    it('still offers Global when the libraries cannot be loaded', async () => {
        const error = vi.spyOn(console, 'error').mockImplementation(() => {});
        mocks.getLibraries.mockRejectedValue(new Error('offline'));
        renderDialog(source('movies-1'));

        await waitFor(() => expect(radio('Movie').disabled).toBe(true));
        expect(radio('Global').checked).toBe(true);
        proceed();
        expect(mocks.onChoose).toHaveBeenCalledWith({ activeTab: 'global' });
        error.mockRestore();
    });

    it('cannot continue until the libraries have loaded', () => {
        mocks.getLibraries.mockReturnValue(new Promise(() => {}));
        renderDialog(source('movies-1'));

        expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    });

    it('names the collection being cloned, and Cancel closes without choosing', async () => {
        renderDialog(source('movies-1'));
        await waitFor(() => expect(radio('Movie').checked).toBe(true));

        expect(screen.getByText('Halloween Movies')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(mocks.onClose).toHaveBeenCalled();
        expect(mocks.onChoose).not.toHaveBeenCalled();
    });
});
