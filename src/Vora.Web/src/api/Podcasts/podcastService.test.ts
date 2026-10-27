import { beforeEach, describe, expect, it, vi } from 'vitest';
import { podcastService, type PodcastFeedEpisodeVM, type PodcastSubscriptionVM } from './podcastService';
import { serverVault, type VoraServer } from '../../utils/serverVault';

interface FakeAnswer {
    status?: number;
    data?: object[];
}

const mocks = vi.hoisted(() => ({ answers: new Map<string, FakeAnswer>() }));

vi.mock('../client', () => ({
    apiClient: {
        get: (url: string, config?: { serverId?: string }) => {
            const answer = mocks.answers.get(`${config?.serverId}${url}`) ?? { data: [] };
            return answer.status ? Promise.reject({ status: answer.status }) : Promise.resolve({ data: answer.data });
        },
    },
    getResponseStatus: (err: { status?: number }) => err.status,
}));

const server = (id: string, name: string): VoraServer =>
    ({ id, name, url: `https://${id}.example.com`, token: `tok-${id}`, profileId: `prof-${id}`, isAdmin: false });

const subscription = (id: string, title: string, feedUrl: string | undefined, subscribedAt: string): PodcastSubscriptionVM =>
    ({ id, showId: `show-${id}`, feedUrl, title, subscribedAt, episodeCount: 3 });

const episode = (id: string, subscriptionId: string, publishedAt: string): PodcastFeedEpisodeVM =>
    ({ id, showId: `show-${subscriptionId}`, subscriptionId, showTitle: 'Show', title: id, audioUrl: `https://cdn.example.com/${id}.mp3`, publishedAt, positionSeconds: 0, isPlayed: false });

const answer = (serverId: string, url: string, result: FakeAnswer) => mocks.answers.set(`${serverId}${url}`, result);

beforeEach(() => {
    localStorage.clear();
    mocks.answers.clear();
    serverVault.addOrUpdateServer(server('home', 'Home'));
    serverVault.addOrUpdateServer(server('cabin', 'Cabin'));
    serverVault.addOrUpdateServer(server('office', 'Office'));
});

describe('podcast subscriptions on every server', () => {
    it('lists a podcast subscribed on two servers once, from the preferred server, newest subscription first', async () => {
        answer('home', '/podcasts/subscriptions', { data: [subscription('h1', 'Daily News', 'https://feeds.example.com/news', '2026-10-01T00:00:00Z')] });
        answer('cabin', '/podcasts/subscriptions', {
            data: [
                subscription('c1', 'Daily News', 'HTTPS://feeds.example.com/news ', '2026-09-01T00:00:00Z'),
                subscription('c2', 'Gardening Hour', 'https://feeds.example.com/garden', '2026-10-20T00:00:00Z'),
            ],
        });

        const result = await podcastService.getSubscriptionsOnEveryServer('cabin');

        expect(result.items.map(sub => [sub.title, sub.serverId, sub.id])).toEqual([
            ['Gardening Hour', 'cabin', 'c2'],
            ['Daily News', 'cabin', 'c1'],
        ]);
        expect(result.items[1].subscribedOn.map(copy => copy.subscriptionId)).toEqual(['c1', 'h1']);
        expect(result.failedServerIds).toEqual([]);
    });

    it('keeps apart the subscriptions of a server too old to send their feed', async () => {
        answer('home', '/podcasts/subscriptions', { data: [subscription('h1', 'Daily News', undefined, '2026-10-01T00:00:00Z')] });
        answer('cabin', '/podcasts/subscriptions', { data: [subscription('c1', 'Daily News', undefined, '2026-09-01T00:00:00Z')] });

        const result = await podcastService.getSubscriptionsOnEveryServer();

        expect(result.items.map(sub => sub.id)).toEqual(['h1', 'c1']);
    });

    it('skips a server with podcasts turned off and reports one it could not reach', async () => {
        answer('home', '/podcasts/subscriptions', { data: [subscription('h1', 'Daily News', 'https://feeds.example.com/news', '2026-10-01T00:00:00Z')] });
        answer('cabin', '/podcasts/subscriptions', { status: 403 });
        answer('office', '/podcasts/subscriptions', { status: 502 });

        const result = await podcastService.getSubscriptionsOnEveryServer();

        expect(result.items.map(sub => sub.id)).toEqual(['h1']);
        expect(result.failedServerIds).toEqual(['office']);
    });
});

describe('new podcast episodes on every server', () => {
    it('lists every server newest first, once per podcast, from the copy the subscriptions list', async () => {
        answer('home', '/podcasts/subscriptions', { data: [subscription('h1', 'Daily News', 'https://feeds.example.com/news', '2026-10-01T00:00:00Z')] });
        answer('home', '/podcasts/episodes/recent', { data: [episode('home-news-26', 'h1', '2026-10-26T06:00:00Z')] });
        answer('cabin', '/podcasts/subscriptions', {
            data: [
                subscription('c1', 'Daily News', 'https://feeds.example.com/news', '2026-09-01T00:00:00Z'),
                subscription('c2', 'Gardening Hour', 'https://feeds.example.com/garden', '2026-10-20T00:00:00Z'),
            ],
        });
        answer('cabin', '/podcasts/episodes/recent', {
            data: [
                episode('cabin-garden-27', 'c2', '2026-10-27T06:00:00Z'),
                episode('cabin-news-26', 'c1', '2026-10-26T06:00:00Z'),
                episode('cabin-garden-20', 'c2', '2026-10-20T06:00:00Z'),
            ],
        });

        const result = await podcastService.getRecentEpisodesOnEveryServer(50, 60, 'home');

        expect(result.items.map(ep => [ep.id, ep.serverId])).toEqual([
            ['cabin-garden-27', 'cabin'],
            ['home-news-26', 'home'],
            ['cabin-garden-20', 'cabin'],
        ]);
    });

    it('keeps the newest episodes up to the limit', async () => {
        answer('home', '/podcasts/subscriptions', { data: [subscription('h1', 'Daily News', 'https://feeds.example.com/news', '2026-10-01T00:00:00Z')] });
        answer('home', '/podcasts/episodes/recent', { data: [episode('home-news-20', 'h1', '2026-10-20T06:00:00Z')] });
        answer('cabin', '/podcasts/subscriptions', { data: [subscription('c2', 'Gardening Hour', 'https://feeds.example.com/garden', '2026-10-20T00:00:00Z')] });
        answer('cabin', '/podcasts/episodes/recent', { data: [episode('cabin-garden-27', 'c2', '2026-10-27T06:00:00Z')] });

        const result = await podcastService.getRecentEpisodesOnEveryServer(1, 60);

        expect(result.items.map(ep => ep.id)).toEqual(['cabin-garden-27']);
    });
});

describe('podcast catalog on every server', () => {
    it('does not count a server with podcasts turned off as unreachable', async () => {
        answer('home', '/podcasts/catalog', { data: [{ showId: 's1', title: 'Daily News', feedUrl: 'https://feeds.example.com/news', isSubscribed: false }] });
        answer('cabin', '/podcasts/catalog', { status: 403 });
        answer('office', '/podcasts/catalog', { status: 502 });

        const result = await podcastService.getAggregatedCatalog();

        expect(result.items.map(item => item.title)).toEqual(['Daily News']);
        expect(result.failedServerIds).toEqual(['office']);
    });
});
