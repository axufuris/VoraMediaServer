import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import FixMatchModal from './FixMatchModal';
import type { MediaMatchCandidate, MediaMatchResult, MediaMatchSource } from '../../api/Media/libraryAdminService';

const searchMatchCandidates = vi.fn<(mediaItemId: string, query?: string, year?: number, serverId?: string) => Promise<MediaMatchCandidate[]>>();
const applyMatch = vi.fn<(mediaItemId: string, source: MediaMatchSource, externalId: string, serverId?: string) => Promise<MediaMatchResult>>();

vi.mock('../../api/Media/libraryAdminService', () => ({
    libraryAdminService: {
        searchMatchCandidates: (mediaItemId: string, query?: string, year?: number, serverId?: string) => searchMatchCandidates(mediaItemId, query, year, serverId),
        applyMatch: (mediaItemId: string, source: MediaMatchSource, externalId: string, serverId?: string) => applyMatch(mediaItemId, source, externalId, serverId),
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
