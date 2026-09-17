import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import MusicAlbumsView from './MusicAlbumsView';
import type { AlbumPageVM, AlbumSortOrder, AlbumVM } from '../../../../api/Music/musicService';

type GetAlbumsOptions = { offset?: number; limit?: number; sort?: AlbumSortOrder; libraryId?: string };

const getAlbums = vi.fn<(options: GetAlbumsOptions, serverId?: string) => Promise<AlbumPageVM>>();

vi.mock('../../../../api/Music/musicService', () => ({
    musicService: {
        getAlbums: (options: GetAlbumsOptions, serverId?: string) => getAlbums(options, serverId),
    },
}));

const album = (n: number): AlbumVM => ({
    id: `album-${n}`,
    title: `Album ${n}`,
    artistId: 'artist-1',
    artistName: 'blink-182',
    isCompilation: false,
    lockedFields: [],
});

const page = (from: number, count: number, total: number): AlbumPageVM => ({
    items: Array.from({ length: count }, (_, i) => album(from + i)),
    total,
    offset: from,
    limit: 60,
});

describe('MusicAlbumsView', () => {
    beforeEach(() => {
        getAlbums.mockReset();
    });

    it('loads the first page recently added first and shows the total', async () => {
        getAlbums.mockResolvedValueOnce(page(0, 2, 2));

        render(<MusicAlbumsView serverId="srv1" refreshKey={0} onOpenAlbum={vi.fn()} />);

        expect(await screen.findByText('2 albums')).toBeInTheDocument();
        expect(getAlbums).toHaveBeenCalledWith({ offset: 0, limit: 60, sort: 'RecentlyAdded' }, 'srv1');
        expect(screen.getAllByRole('button', { name: /blink-182/ })).toHaveLength(2);
        expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
    });

    it('appends the next page from where the loaded albums end', async () => {
        getAlbums.mockResolvedValueOnce(page(0, 60, 61)).mockResolvedValueOnce(page(60, 1, 61));
        render(<MusicAlbumsView refreshKey={0} onOpenAlbum={vi.fn()} />);

        fireEvent.click(await screen.findByRole('button', { name: 'Load more' }));

        await waitFor(() => expect(screen.getAllByRole('button', { name: /blink-182/ })).toHaveLength(61));
        expect(getAlbums).toHaveBeenLastCalledWith({ offset: 60, limit: 60, sort: 'RecentlyAdded' }, undefined);
        expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull();
    });

    it('starts again from the first page when the sort changes', async () => {
        getAlbums.mockResolvedValueOnce(page(0, 60, 100)).mockResolvedValueOnce(page(0, 3, 3));
        render(<MusicAlbumsView refreshKey={0} onOpenAlbum={vi.fn()} />);
        await screen.findByText('100 albums');

        fireEvent.click(screen.getByRole('radio', { name: 'A–Z' }));

        expect(await screen.findByText('3 albums')).toBeInTheDocument();
        expect(getAlbums).toHaveBeenLastCalledWith({ offset: 0, limit: 60, sort: 'Alphabetical' }, undefined);
        expect(screen.getByRole('radio', { name: 'A–Z' })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getAllByRole('button', { name: /blink-182/ })).toHaveLength(3);
    });

    it('opens the album that was clicked', async () => {
        const onOpenAlbum = vi.fn();
        getAlbums.mockResolvedValueOnce(page(0, 1, 1));
        render(<MusicAlbumsView refreshKey={0} onOpenAlbum={onOpenAlbum} />);

        fireEvent.click(await screen.findByRole('button', { name: /blink-182/ }));

        expect(onOpenAlbum).toHaveBeenCalledWith(expect.objectContaining({ id: 'album-0' }));
    });

    it('offers a retry when a page fails to load', async () => {
        getAlbums.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(page(0, 1, 1));
        vi.spyOn(console, 'error').mockImplementation(() => { });
        render(<MusicAlbumsView refreshKey={0} onOpenAlbum={vi.fn()} />);

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(await screen.findByText('1 album')).toBeInTheDocument();
    });

    it('says so when the library has no albums', async () => {
        getAlbums.mockResolvedValueOnce(page(0, 0, 0));

        render(<MusicAlbumsView refreshKey={0} onOpenAlbum={vi.fn()} />);

        expect(await screen.findByText('No albums yet')).toBeInTheDocument();
    });
});
