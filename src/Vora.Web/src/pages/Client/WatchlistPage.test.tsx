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
    default: ({ title, inWatchlist, badge, topLeftBadge }: { title: string; inWatchlist?: boolean; badge?: ReactNode; topLeftBadge?: ReactNode }) => (
        <div data-testid="tile" data-bookmarked={inWatchlist ? 'yes' : undefined} data-has-top-left={topLeftBadge ? 'yes' : undefined}>
            {title}
            <div data-testid="corner">{badge}</div>
        </div>
    ),
}));

const entry = (id: string, title: string): WatchlistItem => ({
    id, profileId: PROFILE, externalId: id, providerId: 'tmdb_discovery', type: 'Movie', title, addedAt: '2026-11-15T00:00:00Z',
});

const titles = () => screen.queryAllByTestId('tile').map(t => t.textContent);

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

    // The corner of each poster, where an episode's number goes, says where the
    // title stands against the libraries: a check in one, a clock requested.
    it('puts a check on a title in the library, a clock on a requested one, and nothing on the rest', async () => {
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

    it('shows the check, not the clock, for a title that is in the library and also requested', async () => {
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

    it("leaves the list alone for another profile's change", async () => {
        mocks.getWatchlist.mockResolvedValue([entry('1', 'Heat')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        expect(await screen.findByText('Heat')).toBeInTheDocument();

        await fire('11111111-0000-0000-0000-000000000000');

        expect(mocks.getWatchlist).toHaveBeenCalledTimes(1);
    });
});
