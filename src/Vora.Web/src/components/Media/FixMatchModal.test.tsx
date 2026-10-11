import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import FixMatchModal from './FixMatchModal';
import type { MediaMatchCandidate, MediaMatchFiles, MediaMatchResult, MediaMatchSource } from '../../api/Media/libraryAdminService';

const searchMatchCandidates = vi.fn<(mediaItemId: string, query?: string, year?: number, serverId?: string) => Promise<MediaMatchCandidate[]>>();
const applyMatch = vi.fn<(mediaItemId: string, source: MediaMatchSource, externalId: string, serverId?: string) => Promise<MediaMatchResult>>();
const getMatchFiles = vi.fn<(mediaItemId: string, serverId?: string) => Promise<MediaMatchFiles>>();

vi.mock('../../api/Media/libraryAdminService', () => ({
    libraryAdminService: {
        searchMatchCandidates: (mediaItemId: string, query?: string, year?: number, serverId?: string) => searchMatchCandidates(mediaItemId, query, year, serverId),
        applyMatch: (mediaItemId: string, source: MediaMatchSource, externalId: string, serverId?: string) => applyMatch(mediaItemId, source, externalId, serverId),
        getMatchFiles: (mediaItemId: string, serverId?: string) => getMatchFiles(mediaItemId, serverId),
    },
}));

const deadCity: MediaMatchCandidate = {
    source: 'tvdb',
    externalId: '417549',
    title: 'The Walking Dead: Dead City',
    year: 2023,
    overview: 'Maggie and Negan travel to Manhattan.',
    posterUrl: null,
    providerName: 'TVDB',
};

const drivein: MediaMatchCandidate = {
    source: 'tvdb',
    externalId: '999',
    title: 'The Last Drive-in: The Walking Dead - Dead City',
    year: 2023,
    providerName: 'TVDB',
};

const renderModal = (overrides: Partial<Parameters<typeof FixMatchModal>[0]> = {}) => {
    const props = {
        mediaItemId: 'dup-show',
        mediaType: 'TvShow' as const,
        serverId: 'srv1',
        onClose: vi.fn(),
        onMatched: vi.fn(),
        ...overrides,
    };
    render(<div data-vora-client=""><FixMatchModal {...props} /></div>);
    return props;
};

describe('fix match', () => {
    beforeEach(() => {
        searchMatchCandidates.mockReset();
        applyMatch.mockReset();
        getMatchFiles.mockReset();
        getMatchFiles.mockResolvedValue({ fileNames: [], folderNames: [] });
    });

    // A blank query lets the server search this item's own title, cleaned of the
    // broken id tag that caused the mismatch in the first place.
    it('searches with the item title and year as soon as it opens', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity]);
        renderModal({ currentYear: 2023 });

        expect(await screen.findByText('The Walking Dead: Dead City')).toBeInTheDocument();
        expect(searchMatchCandidates).toHaveBeenCalledWith('dup-show', '', 2023, 'srv1');
    });

    it('shows each match with its year, provider and id', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity]);
        renderModal();

        expect(await screen.findByText('TVDB · TVDB 417549')).toBeInTheDocument();
        expect(screen.getByText('2023')).toBeInTheDocument();
        expect(screen.getByText('Maggie and Negan travel to Manhattan.')).toBeInTheDocument();
    });

    it('cannot apply until a match is picked', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity, drivein]);
        renderModal();

        await screen.findByText('The Walking Dead: Dead City');

        expect(screen.getByRole('button', { name: 'Apply match' })).toBeDisabled();
    });

    it('applies the picked match and reports where it landed', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity, drivein]);
        const result: MediaMatchResult = { mediaItemId: 'real-show', mergedDuplicate: true };
        applyMatch.mockResolvedValue(result);
        const props = renderModal();

        fireEvent.click(await screen.findByRole('button', { name: /The Walking Dead: Dead City/ }));
        expect(screen.getByRole('button', { name: /The Walking Dead: Dead City/ })).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(screen.getByRole('button', { name: 'Apply match' }));

        await waitFor(() => expect(props.onMatched).toHaveBeenCalledWith(result));
        expect(applyMatch).toHaveBeenCalledWith('dup-show', 'tvdb', '417549', 'srv1');
    });

    it('searches again with what was typed', async () => {
        searchMatchCandidates.mockResolvedValue([]);
        renderModal({ currentYear: 2024 });
        await screen.findByText(/No matches found/);

        searchMatchCandidates.mockResolvedValue([deadCity]);
        fireEvent.change(screen.getByRole('searchbox', { name: 'Search title or id' }), { target: { value: 'tt18546730' } });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Year' }), { target: { value: '' } });
        fireEvent.click(screen.getByRole('button', { name: 'Search' }));

        expect(await screen.findByText('The Walking Dead: Dead City')).toBeInTheDocument();
        expect(searchMatchCandidates).toHaveBeenLastCalledWith('dup-show', 'tt18546730', undefined, 'srv1');
    });

    // A new search throws away the old list, so a pick from it can't be applied
    // against results the viewer no longer sees.
    it('clears the pick when searching again', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity]);
        renderModal();
        fireEvent.click(await screen.findByRole('button', { name: /The Walking Dead: Dead City/ }));

        fireEvent.click(screen.getByRole('button', { name: 'Search' }));
        await screen.findByRole('button', { name: /The Walking Dead: Dead City/ });

        expect(screen.getByRole('button', { name: 'Apply match' })).toBeDisabled();
    });

    it('says when nothing matched and how to try again', async () => {
        searchMatchCandidates.mockResolvedValue([]);
        renderModal();

        expect(await screen.findByText(/paste an IMDb or TMDB link/)).toBeInTheDocument();
    });

    it('shows why a search failed', async () => {
        searchMatchCandidates.mockRejectedValue(new Error('Provider unavailable'));
        renderModal();

        expect(await screen.findByRole('alert')).toHaveTextContent('Search failed: Provider unavailable');
    });

    it('keeps the dialog open and explains a failed apply', async () => {
        searchMatchCandidates.mockResolvedValue([deadCity]);
        applyMatch.mockRejectedValue(new Error('Not a valid TVDB id'));
        const props = renderModal();

        fireEvent.click(await screen.findByRole('button', { name: /The Walking Dead: Dead City/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Apply match' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Could not apply the match: Not a valid TVDB id');
        expect(props.onMatched).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Apply match' })).toBeEnabled();
    });

    it('names the kind of item being matched', async () => {
        searchMatchCandidates.mockResolvedValue([]);
        renderModal({ mediaType: 'Movie' });

        expect(await screen.findByText(/Pick the movie this really is/)).toBeInTheDocument();
    });
});

describe('fix match files on disk', () => {
    const writeText = vi.fn<(text: string) => Promise<void>>();

    beforeEach(() => {
        searchMatchCandidates.mockReset();
        searchMatchCandidates.mockResolvedValue([]);
        getMatchFiles.mockReset();
        writeText.mockReset();
        writeText.mockResolvedValue(undefined);
        Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });
        Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    });

    it('shows the file a movie was scanned from', async () => {
        getMatchFiles.mockResolvedValue({ fileNames: ['The.Matrix.1999.1080p.BluRay.x264.mkv'], folderNames: [] });
        renderModal({ mediaItemId: 'matrix', mediaType: 'Movie' });

        const files = await screen.findByRole('list', { name: 'File on disk' });
        expect(files).toHaveTextContent('The.Matrix.1999.1080p.BluRay.x264.mkv');
        expect(getMatchFiles).toHaveBeenCalledWith('matrix', 'srv1');
    });

    it('lists every version of a movie', async () => {
        getMatchFiles.mockResolvedValue({ fileNames: ['Heat (1995) 1080p.mkv', 'Heat (1995) 2160p.mkv'], folderNames: [] });
        renderModal({ mediaType: 'Movie' });

        expect(await screen.findByText('Heat (1995) 1080p.mkv')).toBeInTheDocument();
        expect(screen.getByText('Heat (1995) 2160p.mkv')).toBeInTheDocument();
    });

    it('shows the folder a show was scanned from', async () => {
        getMatchFiles.mockResolvedValue({ fileNames: [], folderNames: ['The Walking Dead - Dead City [imdb-]'] });
        renderModal();

        const folders = await screen.findByRole('list', { name: 'Folder on disk' });
        expect(folders).toHaveTextContent('The Walking Dead - Dead City [imdb-]');
    });

    it('copies a name to the clipboard', async () => {
        getMatchFiles.mockResolvedValue({ fileNames: ['The.Matrix.1999.1080p.BluRay.x264.mkv'], folderNames: [] });
        renderModal({ mediaType: 'Movie' });

        fireEvent.click(await screen.findByRole('button', { name: 'Copy The.Matrix.1999.1080p.BluRay.x264.mkv' }));

        expect(writeText).toHaveBeenCalledWith('The.Matrix.1999.1080p.BluRay.x264.mkv');
        expect(await screen.findByText('Copied')).toBeInTheDocument();
    });

    it('leaves the name selectable but offers no copy button where the browser blocks the clipboard', async () => {
        Object.defineProperty(window, 'isSecureContext', { value: false, configurable: true });
        getMatchFiles.mockResolvedValue({ fileNames: ['The.Matrix.1999.1080p.BluRay.x264.mkv'], folderNames: [] });
        renderModal({ mediaType: 'Movie' });

        expect(await screen.findByText('The.Matrix.1999.1080p.BluRay.x264.mkv')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Copy/ })).not.toBeInTheDocument();
    });

    it('shows nothing extra when the files cannot be read', async () => {
        getMatchFiles.mockRejectedValue(new Error('offline'));
        renderModal({ mediaType: 'Movie' });

        await waitFor(() => expect(getMatchFiles).toHaveBeenCalled());
        expect(screen.queryByRole('list', { name: 'File on disk' })).not.toBeInTheDocument();
    });
});
