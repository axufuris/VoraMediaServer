import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
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
    default: ({ title }: { title: string }) => <div data-testid="tile">{title}</div>,
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

    it("leaves the list alone for another profile's change", async () => {
        mocks.getWatchlist.mockResolvedValue([entry('1', 'Heat')]);
        render(<MemoryRouter><WatchlistPage /></MemoryRouter>);
        expect(await screen.findByText('Heat')).toBeInTheDocument();

        await fire('11111111-0000-0000-0000-000000000000');

        expect(mocks.getWatchlist).toHaveBeenCalledTimes(1);
    });
});
