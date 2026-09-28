import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import MusicGenreView from './MusicGenreView';
import type { AlbumPageVM, AlbumSortOrder, GenreContentVM } from '../../../../api/Music/musicService';

type GetAlbumsOptions = { offset?: number; limit?: number; sort?: AlbumSortOrder; genre?: string };

const getAlbums = vi.fn<(options: GetAlbumsOptions, serverId?: string) => Promise<AlbumPageVM>>();

vi.mock('../../../../api/Music/musicService', () => ({
    musicService: {
        getAlbums: (options: GetAlbumsOptions, serverId?: string) => getAlbums(options, serverId),
    },
}));

const genre = (overrides: Partial<GenreContentVM> = {}): GenreContentVM => ({
    name: 'Alternative Rock',
    artists: [
        { id: 'a1', name: 'blink-182', lockedFields: [] },
        { id: 'a2', name: 'Weezer', lockedFields: [] },
    ],
    albums: [],
    albumCount: 142,
    tracks: [],
    ...overrides,
});

const albums: AlbumPageVM = {
    items: [{ id: 'al1', title: 'Enema of the State', artistId: 'a1', artistName: 'blink-182', isCompilation: false, lockedFields: [] }],
    total: 142,
    offset: 0,
    limit: 60,
};

const renderGenre = (updateNav = vi.fn()) => {
    render(<MusicGenreView isLoading={false} currentGenre={genre()} refreshKey={0} updateNav={updateNav} />);
    return updateNav;
};

describe('MusicGenreView', () => {
    beforeEach(() => {
        getAlbums.mockReset();
        getAlbums.mockResolvedValue(albums);
    });

    it('counts every album in the genre, not the sample', () => {
        renderGenre();

        expect(screen.getByText('2 artists · 142 albums')).toBeInTheDocument();
    });

    it("pages through the genre's albums and can reorder them", async () => {
        renderGenre();

        await waitFor(() => expect(getAlbums).toHaveBeenCalledWith(expect.objectContaining({ genre: 'Alternative Rock', offset: 0, sort: 'Alphabetical' }), undefined));

        fireEvent.click(screen.getByRole('radio', { name: 'Newest' }));

        await waitFor(() => expect(getAlbums).toHaveBeenCalledWith(expect.objectContaining({ genre: 'Alternative Rock', sort: 'Newest' }), undefined));
    });

    it('draws artists with the standard card and opens one', () => {
        const updateNav = renderGenre();

        fireEvent.click(screen.getByText('Weezer'));

        expect(updateNav).toHaveBeenCalledWith({ view: 'artist', artistId: 'a2' });
    });

    it("heads the page with the genre's own tile cover", () => {
        const { container } = render(<MusicGenreView isLoading={false} currentGenre={genre({ sampleArtworkUrl: '/art/sample.jpg' })} refreshKey={0} updateNav={vi.fn()} />);

        expect(container.querySelector('img[src="/art/sample.jpg"]')).not.toBeNull();
    });
});
