import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import WatchlistPage from './WatchlistPage';
import { type WatchlistItem } from '../../api/Watchlist/watchlistService';

const PROFILE = '8e35a9ec-bb9a-4dfa-b687-00833e862cf9';

const mocks = vi.hoisted(() => ({
    getWatchlist: vi.fn(),
    handlers: new Map<string, (payload: unknown) => void>(),
}));

vi.mock('../../api/Watchlist/watchlistService', () => ({ watchlistService: { getWatchlist: mocks.getWatchlist } }));
vi.mock('../../api/client', () => ({ profileIdForServer: () => PROFILE }));
vi.mock('../../hooks/useSignalREvent', () => ({
    useSignalREvent: (name: string, handler: (payload: unknown) => void) => { mocks.handlers.set(name, handler); },
}));
vi.mock('../../components/Client/Primitives/MediaCard', () => ({
    default: ({ title, captionLines, inWatchlist, badge, topLeftBadge }: { title: string; captionLines?: string[]; inWatchlist?: boolean; badge?: ReactNode; topLeftBadge?: ReactNode }) => (
        <div data-testid="tile" data-bookmarked={inWatchlist ? 'yes' : undefined} data-has-top-left={topLeftBadge ? 'yes' : undefined}>
            {title}
            <div data-testid="corner">{badge}</div>
            <div data-testid="caption">{(captionLines ?? []).join('|')}</div>
        </div>
    ),
}));

const entry = (id: string, title: string): WatchlistItem => ({
    id, profileId: PROFILE, externalId: id, providerId: 'tmdb_discovery', type: 'Movie', title, addedAt: '2026-11-15T00:00:00Z',
});

const titles = () => screen.queryAllByTestId('tile').map(t => t.childNodes[0].textContent);

const fire = async (payload: unknown) => {
    await act(async () => { mocks.handlers.get('WatchlistUpdated')?.(payload); });
};

describe('WatchlistPage', () => {
    beforeEach(() => {
        mocks.getWatchlist.mockReset();
        mocks.handlers.clear();
    });

    it("drops a title removed on any device as soon as the server says this profile's watchlist changed", async () => {
        mocks.getWatchlist.mockResolvedValueOnce([entry('1', 'Heat'), entry('2', 'Ronin')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        expect(await screen.findByText('Ronin')).toBeInTheDocument();

        mocks.getWatchlist.mockResolvedValueOnce([entry('1', 'Heat')]);
        await fire(PROFILE);

        expect(titles()).toEqual(['Heat']);
        expect(mocks.getWatchlist).toHaveBeenCalledTimes(2);
    });

    it('puts a play triangle on a title in the library, a clock on a requested one, and nothing on the rest', async () => {
        mocks.getWatchlist.mockResolvedValue([
            { ...entry('1', 'Heat'), mediaItemId: 'library-heat' },
            { ...entry('2', 'Ronin'), isRequested: true },
            entry('3', 'Collateral'),
        ]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Collateral');

        const [heat, ronin, collateral] = screen.getAllByTestId('corner');
        expect(heat).toHaveTextContent('In your library');
        expect(ronin).toHaveTextContent('Requested');
        expect(collateral).toBeEmptyDOMElement();
    });

    it('shows the triangle, not the clock, for a title that is in the library and also requested', async () => {
        mocks.getWatchlist.mockResolvedValue([{ ...entry('1', 'Heat'), mediaItemId: 'library-heat', isRequested: true }]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getByTestId('corner')).toHaveTextContent('In your library');
        expect(screen.getByTestId('corner')).not.toHaveTextContent('Requested');
    });

    it('keeps the old top-left marker off the posters', async () => {
        mocks.getWatchlist.mockResolvedValue([entry('1', 'Heat')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getByTestId('tile')).not.toHaveAttribute('data-has-top-left');
    });

    it("does not bookmark titles that are all on the watchlist already", async () => {
        mocks.getWatchlist.mockResolvedValue([{ ...entry('1', 'Heat'), mediaItemId: 'library-heat' }, entry('2', 'Ronin')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Ronin');

        for (const tile of screen.getAllByTestId('tile')) expect(tile).not.toHaveAttribute('data-bookmarked');
    });

    it('puts a header over each group, in the order movies in the library, shows in the library, movies not yet in it, shows not yet in it', async () => {
        mocks.getWatchlist.mockResolvedValue([
            { ...entry('1', 'Dune: Part Three'), isRequested: true },
            { ...entry('2', 'Severance'), type: 'TvShow' },
            { ...entry('3', 'Heat'), mediaItemId: 'library-heat' },
            { ...entry('4', 'MobLand'), type: 'TvShow', mediaItemId: 'library-mobland' },
        ]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)).toEqual([
            'Movies in Library',
            'Shows in Library',
            'Movies Not Yet in Library',
            'Shows Not Yet in Library',
        ]);
        expect(titles()).toEqual(['Heat', 'MobLand', 'Dune: Part Three', 'Severance']);
    });

    it('draws a group only when something is in it', async () => {
        mocks.getWatchlist.mockResolvedValue([{ ...entry('1', 'Heat'), mediaItemId: 'library-heat' }]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)).toEqual(['Movies in Library']);
    });

    it('draws no group header for an empty watchlist', async () => {
        mocks.getWatchlist.mockResolvedValue([]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Your watchlist is empty');

        expect(screen.queryAllByRole('heading', { level: 2 })).toEqual([]);
    });

    it('moves a title between groups when the server says it reached the library', async () => {
        mocks.getWatchlist.mockResolvedValueOnce([entry('1', 'Heat')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');
        expect(screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)).toEqual(['Movies Not Yet in Library']);

        mocks.getWatchlist.mockResolvedValueOnce([{ ...entry('1', 'Heat'), mediaItemId: 'library-heat' }]);
        await fire(PROFILE);

        expect(screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)).toEqual(['Movies in Library']);
    });

    it('puts the year under each title, as the home page does, for a movie and for a show', async () => {
        mocks.getWatchlist.mockResolvedValue([
            { ...entry('1', 'Heat'), releaseDate: '1995-12-15' },
            { ...entry('2', 'MobLand'), type: 'TvShow', releaseDate: '2025-03-30' },
        ]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getAllByTestId('caption').map(c => c.textContent)).toEqual(['1995', '2025']);
    });

    it('draws no second line for a title whose year is not known, and never the type', async () => {
        mocks.getWatchlist.mockResolvedValue([entry('1', 'Heat'), { ...entry('2', 'MobLand'), type: 'TvShow' }]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        await screen.findByText('Heat');

        expect(screen.getAllByTestId('caption').map(c => c.textContent)).toEqual(['', '']);
    });

    it("leaves the list alone for another profile's change", async () => {
        mocks.getWatchlist.mockResolvedValue([entry('1', 'Heat')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        expect(await screen.findByText('Heat')).toBeInTheDocument();

        await fire('11111111-0000-0000-0000-000000000000');

        expect(mocks.getWatchlist).toHaveBeenCalledTimes(1);
    });
});
