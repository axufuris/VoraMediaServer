import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ContentRatingBadge from './ContentRatingBadge';
import MusicMetadataEditModal from './MusicMetadataEditModal';
import { hasExplicitTrack } from '../../utils/musicContentRating';
import type { AlbumVM, TrackVM } from '../../api/Music/musicService';

const mocks = vi.hoisted(() => ({
    updateTrack: vi.fn(),
    updateAlbum: vi.fn(),
    setAlbumContentRating: vi.fn(),
}));

vi.mock('../../api/Music/musicService', () => ({ musicService: mocks }));
vi.mock('../../api/System/pluginAdminService', () => ({
    pluginAdminService: { getArtworkProviders: () => Promise.resolve([]) },
}));
vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: () => Promise.resolve(), confirm: () => Promise.resolve(true), prompt: () => Promise.resolve(null) }),
}));

const track = (overrides: Partial<TrackVM> = {}): TrackVM => ({
    id: 't1', title: 'Without Me', trackNumber: 1, isLiked: false, lockedFields: [],
    contentRating: 'Clean', contentRatingSource: 'Provider', ...overrides,
});

const album: AlbumVM = {
    id: 'a1', title: 'The Eminem Show', isCompilation: false, artistId: 'ar1', artistName: 'Eminem', lockedFields: [],
};

const renderModal = (props: Partial<React.ComponentProps<typeof MusicMetadataEditModal>>) => render(
    <MemoryRouter>
        <MusicMetadataEditModal isOpen onClose={() => {}} onSaved={() => {}} kind="track" {...props} />
    </MemoryRouter>,
);

describe('ContentRatingBadge', () => {
    it('marks explicit and clean songs, and nothing else', () => {
        const { rerender, container } = render(<ContentRatingBadge rating="Explicit" />);
        expect(screen.getByRole('img', { name: 'Explicit' })).toHaveTextContent('E');

        rerender(<ContentRatingBadge rating="Clean" />);
        expect(screen.getByRole('img', { name: 'Clean' })).toHaveTextContent('C');

        rerender(<ContentRatingBadge rating={null} />);
        expect(container).toBeEmptyDOMElement();
    });

    it('calls an album explicit when any track is', () => {
        expect(hasExplicitTrack([{ contentRating: 'Clean' }, { contentRating: 'Explicit' }])).toBe(true);
        expect(hasExplicitTrack([{ contentRating: 'Clean' }, {}])).toBe(false);
    });
});

describe('editing a music rating', () => {
    beforeEach(() => {
        mocks.updateTrack.mockReset().mockResolvedValue(undefined);
        mocks.updateAlbum.mockReset().mockResolvedValue(undefined);
        mocks.setAlbumContentRating.mockReset().mockResolvedValue(undefined);
    });

    it('offers only Explicit, Clean or None for a track, and says where its rating came from', () => {
        renderModal({ track: track() });

        expect(screen.getByRole('button', { name: 'Clean' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByRole('button', { name: 'None' })).toBeInTheDocument();
        expect(screen.queryByRole('textbox', { name: /content rating/i })).toBeNull();
        expect(screen.getByText('Looked up online because the file carries no rating.')).toBeInTheDocument();
    });

    it('warns that saving locks a changed rating, and sends it', async () => {
        renderModal({ track: track() });

        fireEvent.click(screen.getByRole('button', { name: 'Explicit' }));
        expect(screen.getByText(/Saving locks this rating/)).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(mocks.updateTrack).toHaveBeenCalled());
        expect(mocks.updateTrack.mock.calls[0][1]).toMatchObject({ contentRating: 'Explicit' });
    });

    it('sends no rating when None is chosen', async () => {
        renderModal({ track: track({ contentRating: 'Explicit', contentRatingSource: 'FileTag' }) });

        fireEvent.click(screen.getByRole('button', { name: 'None' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(mocks.updateTrack).toHaveBeenCalled());
        expect(mocks.updateTrack.mock.calls[0][1]).toMatchObject({ contentRating: null });
    });

    it('leaves an album\'s tracks alone unless a rating is chosen for all of them', async () => {
        renderModal({ kind: 'album', album });

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(mocks.updateAlbum).toHaveBeenCalled());
        expect(mocks.setAlbumContentRating).not.toHaveBeenCalled();
    });

    it('rates every track of an album at once', async () => {
        renderModal({ kind: 'album', album });

        fireEvent.click(screen.getByRole('button', { name: 'Explicit' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => expect(mocks.setAlbumContentRating).toHaveBeenCalledWith('a1', 'Explicit', undefined));
    });
});
