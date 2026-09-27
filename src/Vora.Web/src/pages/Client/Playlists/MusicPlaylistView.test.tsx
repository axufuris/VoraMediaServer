import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MusicPlaylistView from './MusicPlaylistView';
import PlaylistCover from '../../../components/Collections/PlaylistCover';
import { playlistTileArt } from '../../../utils/playlistArt';
import type { PlaylistDetailsVM, PlaylistItemVM } from '../../../api/Collections/playlistService';

const song = (n: number, overrides: Partial<PlaylistItemVM> = {}): PlaylistItemVM => ({
    id: `i${n}`, mediaItemId: `t${n}`, order: n, title: `Song ${n}`, type: 'Track',
    isPlayed: false, resumePositionSeconds: 0, artistName: 'blink-182', albumTitle: 'Wasting Time',
    durationSeconds: 166, ...overrides,
});

const playlist = (overrides: Partial<PlaylistDetailsVM> = {}): PlaylistDetailsVM => ({
    id: 'p1', name: 'TEST', mediaType: 'Music', itemCount: 2, posterUrls: ['/a.jpg'], backdropUrls: [],
    isShared: true, isOwner: true, ownerName: 'Andy', items: [song(1), song(2)], ...overrides,
});

const renderView = (p: PlaylistDetailsVM, handlers: { onPlay?: (i: number, s: boolean) => void; onRemove?: (id: string) => void } = {}) => render(
    <MemoryRouter>
        <MusicPlaylistView
            playlist={p}
            canEdit={p.isOwner}
            headerActions={null}
            onPlay={handlers.onPlay ?? vi.fn()}
            onRemove={handlers.onRemove ?? vi.fn()}
            draggedIndex={null}
            onDragStart={vi.fn()}
            onDragOver={vi.fn()}
            onDrop={vi.fn()}
        />
    </MemoryRouter>,
);

describe('MusicPlaylistView', () => {
    it("headlines the playlist, not a song, and says how long it is", () => {
        renderView(playlist());

        expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('TEST');
        expect(screen.getByText('2 songs • 6 min')).toBeInTheDocument();
    });

    // Watched state and View Details mean nothing for a song.
    it('has no watched checkmarks, Unwatch All or View Details', () => {
        renderView(playlist());

        expect(screen.queryByText(/unwatch/i)).toBeNull();
        expect(screen.queryByTitle(/mark as/i)).toBeNull();
        expect(screen.queryByTitle(/view details/i)).toBeNull();
    });

    it('plays from the song clicked, and shuffles from the Shuffle button', () => {
        const onPlay = vi.fn();
        renderView(playlist(), { onPlay });

        fireEvent.click(screen.getByText('Song 2'));
        fireEvent.click(screen.getByRole('button', { name: 'Shuffle' }));

        expect(onPlay).toHaveBeenNthCalledWith(1, 1, false);
        expect(onPlay).toHaveBeenNthCalledWith(2, 0, true);
    });

    it('lets the owner remove a song, and only the owner', () => {
        const onRemove = vi.fn();
        const { unmount } = renderView(playlist(), { onRemove });

        fireEvent.click(screen.getByRole('button', { name: 'Remove Song 1 from the playlist' }));
        expect(onRemove).toHaveBeenCalledWith('i1');
        unmount();

        renderView(playlist({ isOwner: false }));
        expect(screen.queryByRole('button', { name: /remove/i })).toBeNull();
    });

    it('offers to add every song to another playlist', () => {
        renderView(playlist());

        expect(screen.getAllByRole('button', { name: /to a playlist$/ })).toHaveLength(2);
    });

    it('explains an empty playlist', () => {
        renderView(playlist({ items: [], itemCount: 0 }));

        expect(screen.getByText(/No songs yet/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Play' })).toBeNull();
    });
});

describe('playlist covers', () => {
    it("uses the owner's image over any mosaic", () => {
        const { container } = render(<PlaylistCover imageUrl="/mine.png" posterUrls={['/a', '/b', '/c', '/d']} shape="square" />);

        expect(container.querySelectorAll('img')).toHaveLength(1);
        expect(container.querySelector('img')).toHaveAttribute('src', '/mine.png');
    });

    it('makes a four-image mosaic from four different images, and one image otherwise', () => {
        const four = render(<PlaylistCover posterUrls={['/a', '/b', '/c', '/d']} shape="square" />);
        expect(four.container.querySelectorAll('img')).toHaveLength(4);
        four.unmount();

        const repeats = render(<PlaylistCover posterUrls={['/a', '/a', '/a', '/a']} shape="square" />);
        expect(repeats.container.querySelectorAll('img')).toHaveLength(1);
    });

    it("gives a tile the owner's cover instead of the mosaic", () => {
        const base = { id: 'p', name: 'x', mediaType: 'Music' as const, itemCount: 1, backdropUrls: [], isShared: false, isOwner: true, ownerName: '' };

        expect(playlistTileArt({ ...base, imageUrl: '/mine.png', posterUrls: ['/a', '/b'] })).toEqual({ imageUrl: '/mine.png' });
        expect(playlistTileArt({ ...base, posterUrls: ['/a', '/b'] })).toEqual({ mosaicUrls: ['/a', '/b'], imageUrl: '/a' });
    });
});
