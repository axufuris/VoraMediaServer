import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import DiscoveryViewAllPage from './DiscoveryViewAllPage';
import type { DiscoveryItem } from '../../../api/Discovery/discoveryService';

const PROFILE = '8e35a9ec-bb9a-4dfa-b687-00833e862cf9';

const mocks = vi.hoisted(() => ({
    getAdminConfigs: vi.fn(),
    getRowItems: vi.fn(),
    getWatchlist: vi.fn(),
    items: [] as DiscoveryItem[],
}));

vi.mock('../../../api/Discovery/discoveryService', () => ({
    discoveryService: { getAdminConfigs: mocks.getAdminConfigs, getRowItems: mocks.getRowItems },
}));
vi.mock('../../../api/Watchlist/watchlistService', () => ({ watchlistService: { getWatchlist: mocks.getWatchlist } }));
vi.mock('../../../api/client', () => ({ profileIdForServer: () => PROFILE }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
vi.mock('../../../components/Client/Primitives/MediaCard', () => ({
    default: ({ title, inWatchlist, badge, bottomLeftBadge }: { title: string; inWatchlist?: boolean; badge?: ReactNode; bottomLeftBadge?: ReactNode }) => (
        <div
            data-testid="tile"
            data-old-bookmark={inWatchlist ? 'yes' : undefined}
            data-old-pill={bottomLeftBadge ? 'yes' : undefined}
        >
            {title}
            <div data-testid="corner">{badge}</div>
        </div>
    ),
}));

const item = (id: string, extra: Partial<DiscoveryItem> = {}): DiscoveryItem => ({
    externalId: id, providerId: 'tmdb_discovery', type: 'Movie', title: `Title ${id}`, ...extra,
} as DiscoveryItem);

const corners = () => screen.getAllByTestId('corner');

const renderPage = async () => {
    render(
        <MemoryRouter initialEntries={['/discovery/tmdb_discovery/row/popular']}>
            <Routes><Route path="/discovery/:providerId/row/:rowId" element={<DiscoveryViewAllPage />} /></Routes>
        </MemoryRouter>
    );
    await screen.findAllByTestId('tile');
};

describe('DiscoveryViewAllPage tile corners', () => {
    beforeEach(() => {
        vi.stubGlobal('IntersectionObserver', class { observe() { } disconnect() { } unobserve() { } });
        mocks.getAdminConfigs.mockResolvedValue([{ providerId: 'tmdb_discovery', rowId: 'popular', name: 'Popular Movies', isEnabled: true }]);
        mocks.getWatchlist.mockResolvedValue([]);
        mocks.getRowItems.mockImplementation((_p: string, _r: string, page: number) => Promise.resolve(page === 1 ? mocks.items : []));
    });

    it('says in your library with an icon, not the old pill', async () => {
        mocks.items = [item('1', { inLibrary: true })];
        await renderPage();

        expect(corners()[0]).toHaveTextContent('In your library');
        expect(screen.getByTestId('tile')).not.toHaveAttribute('data-old-pill');
    });

    it('says requested for a request that is on its way, and nothing for a denied one', async () => {
        mocks.items = [item('1', { requestStatus: 'Pending' }), item('2', { requestStatus: 'Processing' }), item('3', { requestStatus: 'Denied' }), item('4')];
        await renderPage();

        const [pending, processing, denied, plain] = corners();
        expect(pending).toHaveTextContent('Requested');
        expect(processing).toHaveTextContent('Requested');
        expect(denied).toBeEmptyDOMElement();
        expect(plain).toBeEmptyDOMElement();
    });

    it('treats a fulfilled request as in your library', async () => {
        mocks.items = [item('1', { requestStatus: 'Available' })];
        await renderPage();

        expect(corners()[0]).toHaveTextContent('In your library');
    });

    it('puts the watchlist bookmark in the same corner, beside the status, and not in the old top-left spot', async () => {
        mocks.items = [item('1', { requestStatus: 'Pending' }), item('2'), item('3', { inLibrary: true })];
        mocks.getWatchlist.mockResolvedValue([
            { providerId: 'tmdb_discovery', externalId: '1' },
            { providerId: 'tmdb_discovery', externalId: '2' },
        ]);
        await renderPage();
        await screen.findAllByText('On your watchlist');

        const [requestedAndSaved, savedOnly, libraryOnly] = corners();
        expect(requestedAndSaved).toHaveTextContent('On your watchlist');
        expect(requestedAndSaved).toHaveTextContent('Requested');
        expect(savedOnly).toHaveTextContent('On your watchlist');
        expect(savedOnly).not.toHaveTextContent('Requested');
        expect(libraryOnly).not.toHaveTextContent('On your watchlist');
        screen.getAllByTestId('tile').forEach(t => expect(t).not.toHaveAttribute('data-old-bookmark'));
    });
});
