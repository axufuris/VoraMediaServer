import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import MusicAlbumView from './MusicAlbumView';
import type { AlbumVM, TrackVM } from '../../../../api/Music/musicService';

const album = (overrides: Partial<AlbumVM> = {}): AlbumVM => ({
    id: 'a1', title: 'Crash My Party', isCompilation: false, artistId: 'ar1', artistName: 'Luke Bryan', lockedFields: [], ...overrides,
});

const track = (overrides: Partial<TrackVM> = {}): TrackVM => ({
    id: 't1', title: 'Play It Again', trackNumber: 1, isLiked: false, lockedFields: [], ...overrides,
});

const renderAlbum = (currentAlbum: AlbumVM, tracks: TrackVM[]) => render(
    <MusicAlbumView
        isLoading={false}
        currentAlbum={currentAlbum}
        albumBackdrop={null}
        tracks={tracks}
        isServerAdmin={false}
        playFromIndex={vi.fn()}
        playWholeAlbum={vi.fn()}
        startRadioFromSeed={vi.fn()}
        handleSetAlbumRating={vi.fn()}
        handleSetTrackRating={vi.fn()}
        toggleTrackLike={vi.fn()}
        onEditAlbum={vi.fn()}
        onEditTrack={vi.fn()}
        onTrackContextMenu={vi.fn()}
        formatDuration={() => '3:00'}
    />,
);

describe('MusicAlbumView popularity', () => {
    it("shows the album's plays beside the Last.fm mark, with the full figure on hover", () => {
        renderAlbum(album({ globalPlays: 4_100_000 }), [track()]);

        const figure = screen.getByTitle('4,100,000 plays on Last.fm');
        expect(figure).toHaveTextContent('4.1M');
        expect(within(figure).getByRole('img', { name: 'Last.fm' })).toBeInTheDocument();
    });

    it('says nothing before popularity has been fetched', () => {
        renderAlbum(album({ globalPlays: null }), [track()]);

        expect(screen.queryByRole('img', { name: 'Last.fm' })).toBeNull();
    });

    it("shows each track's listeners where Last.fm has them", () => {
        renderAlbum(album(), [track({ globalListeners: 700_000 }), track({ id: 't2', title: 'Deep Cut', trackNumber: 2 })]);

        const figure = screen.getByTitle('700,000 listeners on Last.fm');
        expect(figure).toHaveTextContent('700K');
        expect(within(figure).getByRole('img', { name: 'Last.fm' })).toBeInTheDocument();
        // The track Last.fm doesn't know gets no mark: one mark in the whole list.
        expect(screen.getAllByRole('img', { name: 'Last.fm' })).toHaveLength(1);
    });
});
