import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SearchPage from './SearchPage';
import type { AggregatedGlobalSearchResponse } from '../../api/Discovery/searchService';
import type { DiscoveryItem } from '../../api/Discovery/discoveryService';

const mocks = vi.hoisted(() => ({
    searchAllServers: vi.fn(),
    discoverySearch: vi.fn(),
}));

vi.mock('../../api/Discovery/searchService', () => ({ searchService: { searchAllServers: mocks.searchAllServers } }));
vi.mock('../../api/Discovery/discoveryService', () => ({ discoveryService: { search: mocks.discoverySearch } }));

const local = (titles: string[]): AggregatedGlobalSearchResponse => ({
    query: 'marvel',
    movies: titles.map((title, i) => ({ id: `m${i}`, title, serverId: 's1', serverName: 'Home' }) as unknown as AggregatedGlobalSearchResponse['movies'][number]),
    tvShows: [],
    actors: [],
    collections: [],
    music: [],
    failedServerIds: [],
    serverCount: 1,
});

const hit = (externalId: string, title: string, inLibrary: boolean): DiscoveryItem => ({
    externalId,
    providerId: 'tmdb_discovery',
    title,
    type: 'Movie',
    year: 2019,
    inLibrary,
});

const renderSearch = () => render(
    <MemoryRouter initialEntries={['/search?q=marvel']}>
        <Routes>
            <Route path="/search" element={<SearchPage />} />
        </Routes>
    </MemoryRouter>,
);

describe('SearchPage discovery results', () => {
    beforeEach(() => {
        mocks.searchAllServers.mockReset();
        mocks.discoverySearch.mockReset();
    });

    it('hides only what the server marks as in the library', async () => {
        mocks.searchAllServers.mockResolvedValue(local(['Captain Marvel']));
        mocks.discoverySearch.mockResolvedValue([
            hit('299537', 'Captain Marvel', true),
            hit('609681', 'The Marvels', true),
            hit('1003596', 'Marvel Studios: Assembled', false),
        ]);

        renderSearch();

        expect(await screen.findByText('Discovery (not in library)')).toBeInTheDocument();
        expect(screen.getByText('Marvel Studios: Assembled')).toBeInTheDocument();
        expect(screen.queryByText('The Marvels')).not.toBeInTheDocument();
        // Captain Marvel shows once: the library's copy, not a discovery hit.
        expect(screen.getAllByText('Captain Marvel')).toHaveLength(1);
    });

    it('keeps a title the library does not have even when a local title shares its name', async () => {
        mocks.searchAllServers.mockResolvedValue(local(['Heat']));
        mocks.discoverySearch.mockResolvedValue([hit('42', 'Heat', false)]);

        renderSearch();

        expect(await screen.findByText('Discovery (not in library)')).toBeInTheDocument();
        expect(screen.getAllByText('Heat')).toHaveLength(2);
    });
});
