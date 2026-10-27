import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PodcastsTab from './PodcastsTab';
import type { PodcastEpisodeVM, ServerPodcastFeedEpisode, ServerPodcastSubscription } from '../../../api/Podcasts/podcastService';
import { serverVault, type VoraServer } from '../../../utils/serverVault';

const mocks = vi.hoisted(() => ({
    getSubscriptionsOnEveryServer: vi.fn(),
    getRecentEpisodesOnEveryServer: vi.fn(),
    getEpisodes: vi.fn(),
    unsubscribe: vi.fn(),
    saveEpisodeState: vi.fn(),
    playMedia: vi.fn(),
    confirm: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../../api/Podcasts/podcastService', () => ({
    podcastService: {
        getSubscriptionsOnEveryServer: mocks.getSubscriptionsOnEveryServer,
        getRecentEpisodesOnEveryServer: mocks.getRecentEpisodesOnEveryServer,
        getEpisodes: mocks.getEpisodes,
        unsubscribe: mocks.unsubscribe,
        saveEpisodeState: mocks.saveEpisodeState,
        getAggregatedCatalog: () => Promise.resolve({ items: [], failedServerIds: [] }),
    },
}));
vi.mock('../../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: mocks.playMedia }) }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ alert: mocks.alert, confirm: mocks.confirm }) }));

const server = (id: string, name: string): VoraServer =>
    ({ id, name, url: `https://${id}.example.com`, token: `tok-${id}`, profileId: `prof-${id}`, isAdmin: false });

const news: ServerPodcastSubscription = {
    id: 'h1', showId: 'show-h1', feedUrl: 'https://feeds.example.com/news', title: 'Daily News', subscribedAt: '2026-10-01T00:00:00Z', episodeCount: 3,
    serverId: 'home', serverName: 'Home',
    subscribedOn: [{ serverId: 'home', serverName: 'Home', subscriptionId: 'h1' }, { serverId: 'cabin', serverName: 'Cabin', subscriptionId: 'c1' }],
};

const garden: ServerPodcastSubscription = {
    id: 'c2', showId: 'show-c2', feedUrl: 'https://feeds.example.com/garden', title: 'Gardening Hour', subscribedAt: '2026-10-20T00:00:00Z', episodeCount: 8,
    serverId: 'cabin', serverName: 'Cabin',
    subscribedOn: [{ serverId: 'cabin', serverName: 'Cabin', subscriptionId: 'c2' }],
};

const gardenEpisode: PodcastEpisodeVM = {
    id: 'garden-27', showId: 'show-c2', title: 'Planting garlic', audioUrl: 'https://cdn.example.com/garden-27.mp3', publishedAt: '2026-10-27T06:00:00Z', positionSeconds: 0, isPlayed: false,
};

const recentGarden: ServerPodcastFeedEpisode = {
    ...gardenEpisode, subscriptionId: 'c2', showTitle: 'Gardening Hour', serverId: 'cabin',
};

const renderTab = (view: 'subscriptions' | 'recent') => {
    sessionStorage.setItem('podcast_view_mode', view);
    return render(<MemoryRouter><PodcastsTab /></MemoryRouter>);
};

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    serverVault.addOrUpdateServer(server('home', 'Home'));
    serverVault.addOrUpdateServer(server('cabin', 'Cabin'));
    serverVault.setActiveServerId('home');
    mocks.getSubscriptionsOnEveryServer.mockResolvedValue({ items: [garden, news], failedServerIds: [] });
    mocks.getRecentEpisodesOnEveryServer.mockResolvedValue({ items: [recentGarden], failedServerIds: [] });
    mocks.getEpisodes.mockResolvedValue([gardenEpisode]);
    mocks.unsubscribe.mockResolvedValue(undefined);
    mocks.saveEpisodeState.mockResolvedValue(undefined);
    mocks.confirm.mockResolvedValue(true);
    mocks.alert.mockResolvedValue(undefined);
});

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
});

describe('PodcastsTab subscriptions', () => {
    it('opens a podcast from another server and plays it from that server', async () => {
        renderTab('subscriptions');

        fireEvent.click(await screen.findByText('Gardening Hour'));
        fireEvent.click(await screen.findByText('Planting garlic'));

        expect(mocks.getSubscriptionsOnEveryServer).toHaveBeenCalledWith('home');
        expect(mocks.getEpisodes).toHaveBeenCalledWith('c2', 100, 'cabin');
        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'garden-27', serverId: 'cabin' }));
    });

    it('unsubscribes a podcast on every server it is subscribed on', async () => {
        renderTab('subscriptions');
        await screen.findByText('Daily News');

        fireEvent.click(screen.getAllByTitle('Unsubscribe')[1]);

        await waitFor(() => expect(mocks.unsubscribe).toHaveBeenCalledTimes(2));
        expect(mocks.confirm).toHaveBeenCalledWith('Unsubscribe from "Daily News" on Home and Cabin?');
        expect(mocks.unsubscribe).toHaveBeenCalledWith('h1', 'home');
        expect(mocks.unsubscribe).toHaveBeenCalledWith('c1', 'cabin');
        expect(mocks.alert).not.toHaveBeenCalled();
    });

    it('names the server it could not unsubscribe on', async () => {
        mocks.unsubscribe.mockImplementation((_id: string, serverId: string) => serverId === 'cabin' ? Promise.reject(new Error('offline')) : Promise.resolve());
        renderTab('subscriptions');
        await screen.findByText('Daily News');

        fireEvent.click(screen.getAllByTitle('Unsubscribe')[1]);

        await waitFor(() => expect(mocks.alert).toHaveBeenCalledWith("Couldn't unsubscribe on Cabin."));
    });

    it('says when a server could not be reached', async () => {
        mocks.getSubscriptionsOnEveryServer.mockResolvedValue({ items: [news], failedServerIds: ['cabin'] });
        renderTab('subscriptions');

        expect(await screen.findByText("Couldn't reach 1 server — showing partial results.")).toBeTruthy();
    });
});

describe('PodcastsTab new episodes', () => {
    it('plays an episode from the server it came from', async () => {
        renderTab('recent');

        fireEvent.click(await screen.findByText('Planting garlic'));

        expect(mocks.getRecentEpisodesOnEveryServer).toHaveBeenCalledWith(50, 60, 'home');
        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'garden-27', serverId: 'cabin' }));
    });

    it('marks an episode played on the server it came from', async () => {
        renderTab('recent');

        fireEvent.contextMenu(await screen.findByText('Planting garlic'));
        fireEvent.click(screen.getByText('Mark as played'));

        await waitFor(() => expect(mocks.saveEpisodeState).toHaveBeenCalledWith('garden-27', 0, true, 'cabin'));
    });
});
