import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import SearchBar from './SearchBar';
import type { AggregatedGlobalSearchResponse } from '../../api/Discovery/searchService';
import type { DiscoveryItem } from '../../api/Discovery/discoveryService';

const mocks = vi.hoisted(() => ({
    searchAllServers: vi.fn(),
    discoverySearch: vi.fn(),
    discover: { value: true },
}));

vi.mock('../../api/Discovery/searchService', () => ({ searchService: { searchAllServers: mocks.searchAllServers } }));
vi.mock('../../api/Discovery/discoveryService', () => ({ discoveryService: { search: mocks.discoverySearch } }));
vi.mock('../../hooks/useFeatureFlags', () => ({ useFeatureFlags: () => ({ discover: mocks.discover.value }) }));
vi.mock('../../pages/Client/Audio/Music/storeMusicNav', () => ({ storeMusicNav: vi.fn() }));

const library = (...titles: string[]): AggregatedGlobalSearchResponse => ({
    query: 'blade',
    movies: [],
    tvShows: titles.map((title, index) => ({ id: `s${index}`, type: 'TvShow', title, serverId: 'home', serverName: 'Home' })),
    actors: [],
    collections: [],
    music: [],
    failedServerIds: [],
    serverCount: 1,
});

const discoveryHit = (externalId: string, title: string): DiscoveryItem => ({ externalId, providerId: 'tmdb_discovery', title, type: 'Movie', year: 1982 });

const forbidden = () => new AxiosError('Request failed with status code 403', 'ERR_BAD_REQUEST', undefined, undefined,
    { status: 403, statusText: 'Forbidden', headers: {}, config: { headers: new AxiosHeaders() }, data: '' });

const type = (text: string) => fireEvent.change(screen.getByPlaceholderText('Search movies, shows, actors...'), { target: { value: text } });

const renderBar = () => render(<MemoryRouter><SearchBar /></MemoryRouter>);

const SETTLE = { timeout: 3000 };

describe('SearchBar', () => {
    beforeEach(() => {
        mocks.searchAllServers.mockReset();
        mocks.discoverySearch.mockReset();
        mocks.discover.value = true;
        vi.spyOn(console, 'error').mockImplementation(() => undefined);
    });

    it('lists what the library has, then what Discover has beyond it', async () => {
        mocks.searchAllServers.mockResolvedValue(library('Blade Runner 2099'));
        mocks.discoverySearch.mockResolvedValue([discoveryHit('1', 'Blade Runner 2099'), discoveryHit('78', 'Blade Runner')]);
        renderBar();

        type('Blade');

        expect(await screen.findByText('Blade Runner 2099', undefined, SETTLE)).toBeInTheDocument();
        expect(screen.getByText('Blade Runner')).toBeInTheDocument();
        expect(screen.getAllByText('Blade Runner 2099')).toHaveLength(1);
        expect(screen.getByText('Discovery')).toBeInTheDocument();
    });

    it('still shows the library results when Discover refuses the search', async () => {
        mocks.searchAllServers.mockResolvedValue(library('Blade Runner 2099'));
        mocks.discoverySearch.mockRejectedValue(forbidden());
        renderBar();

        type('Blade');

        expect(await screen.findByText('Blade Runner 2099', undefined, SETTLE)).toBeInTheDocument();
        expect(screen.queryByText('No results found.')).toBeNull();
        expect(screen.queryByText('Discovery')).toBeNull();
        expect(console.error).toHaveBeenCalledWith('Discovery search failed', expect.anything());
    });

    it('does not ask a server that has Discover off to search it', async () => {
        mocks.discover.value = false;
        mocks.searchAllServers.mockResolvedValue(library('Blade Runner 2099'));
        renderBar();

        type('Blade');

        expect(await screen.findByText('Blade Runner 2099', undefined, SETTLE)).toBeInTheDocument();
        expect(mocks.discoverySearch).not.toHaveBeenCalled();
    });

    it('says when nothing is found', async () => {
        mocks.searchAllServers.mockResolvedValue(library());
        mocks.discoverySearch.mockResolvedValue([]);
        renderBar();

        type('Blade');

        expect(await screen.findByText('No results found.', undefined, SETTLE)).toBeInTheDocument();
    });

    it('says when the library search itself fails, rather than that nothing was found', async () => {
        mocks.searchAllServers.mockRejectedValue(new Error('network down'));
        mocks.discoverySearch.mockResolvedValue([]);
        renderBar();

        type('Blade');

        expect(await screen.findByText("Couldn't search right now. Try again in a moment.", undefined, SETTLE)).toBeInTheDocument();
        expect(screen.queryByText('No results found.')).toBeNull();
    });

    it('closes the list, and forgets its results, when the text is cut back under three characters', async () => {
        mocks.searchAllServers.mockResolvedValue(library('Blade Runner 2099'));
        mocks.discoverySearch.mockResolvedValue([]);
        renderBar();
        type('Blade');
        await screen.findByText('Blade Runner 2099', undefined, SETTLE);

        type('Bl');

        expect(screen.queryByText('Blade Runner 2099')).toBeNull();
        expect(screen.queryByText(/Top results for/)).toBeNull();
    });

    it('does not search for fewer than three characters', async () => {
        renderBar();

        type('Bl');
        await new Promise(resolve => setTimeout(resolve, 600));

        expect(mocks.searchAllServers).not.toHaveBeenCalled();
        expect(mocks.discoverySearch).not.toHaveBeenCalled();
    });

    it('drops a slow answer to an earlier search instead of showing it over the newer one', async () => {
        let answerEarlier: (value: AggregatedGlobalSearchResponse) => void = () => undefined;
        mocks.searchAllServers
            .mockImplementationOnce(() => new Promise<AggregatedGlobalSearchResponse>(resolve => { answerEarlier = resolve; }))
            .mockResolvedValue(library('Blade Runner 2099'));
        mocks.discoverySearch.mockResolvedValue([]);
        renderBar();

        type('Bla');
        await waitFor(() => expect(mocks.searchAllServers).toHaveBeenCalledTimes(1), SETTLE);
        type('Blade');
        expect(await screen.findByText('Blade Runner 2099', undefined, SETTLE)).toBeInTheDocument();
        answerEarlier(library('Black Mirror'));
        await new Promise(resolve => setTimeout(resolve, 50));

        expect(screen.queryByText('Black Mirror')).toBeNull();
        expect(screen.getByText('Blade Runner 2099')).toBeInTheDocument();
    });
});
