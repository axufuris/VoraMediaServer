import { apiClient, getResponseStatus } from '../client';
import { serverVault, type VoraServer } from '../../utils/serverVault';
import { serverTimeMs } from '../../utils/serverTime';

export interface PodcastSubscriptionVM {
    id: string;
    showId: string;
    feedUrl?: string;
    title: string;
    author?: string;
    description?: string;
    artworkUrl?: string;
    homepageUrl?: string;
    subscribedAt: string;
    lastRefreshedAt?: string;
    episodeCount: number;
}

export interface PodcastEpisodeVM {
    id: string;
    showId: string;
    title: string;
    description?: string;
    audioUrl: string;
    artworkUrl?: string;
    durationSeconds?: number;
    publishedAt?: string;
    episodeNumber?: number;
    seasonNumber?: number;
    positionSeconds: number;
    isPlayed: boolean;
}

export interface DiscoveredPodcastVM {
    title: string;
    author?: string;
    feedUrl: string;
    artworkUrl?: string;
    description?: string;
    homepageUrl?: string;
    providerName: string;
}

export interface CatalogPodcastVM {
    showId: string;
    title: string;
    author?: string;
    description?: string;
    feedUrl: string;
    artworkUrl?: string;
    homepageUrl?: string;
    isSubscribed: boolean;
}

export interface CatalogServerAvailability {
    serverId: string;
    serverName: string;
    showId: string;
    isSubscribed: boolean;
}

export interface AggregatedCatalogPodcastVM {
    feedUrl: string;
    title: string;
    author?: string;
    description?: string;
    artworkUrl?: string;
    homepageUrl?: string;
    availableOn: CatalogServerAvailability[];
}

export interface AggregatedCatalogResult {
    items: AggregatedCatalogPodcastVM[];
    failedServerIds: string[];
}

export interface PodcastFeedEpisodeVM {
    id: string;
    showId: string;
    subscriptionId: string;
    showTitle: string;
    showArtworkUrl?: string;
    title: string;
    description?: string;
    audioUrl: string;
    artworkUrl?: string;
    durationSeconds?: number;
    publishedAt?: string;
    episodeNumber?: number;
    seasonNumber?: number;
    positionSeconds: number;
    isPlayed: boolean;
}

export interface PodcastSubscriptionCopy {
    serverId: string;
    serverName: string;
    subscriptionId: string;
}

export interface ServerPodcastSubscription extends PodcastSubscriptionVM {
    serverId: string;
    serverName: string;
    subscribedOn: PodcastSubscriptionCopy[];
}

export interface ServerPodcastFeedEpisode extends PodcastFeedEpisodeVM {
    serverId: string;
}

export interface EveryServerResult<T> {
    items: T[];
    failedServerIds: string[];
}

interface ServerAnswer<T> {
    server: VoraServer;
    data: T;
}

// A server with podcasts turned off answers 403. It has nothing to list, and it
// is not a server the page failed to reach.
async function askEveryServer<T>(request: (serverId: string) => Promise<T>, preferredServerId?: string): Promise<{ answers: ServerAnswer<T>[]; failedServerIds: string[] }> {
    const servers = [...serverVault.getServers()]
        .sort((a, b) => Number(b.id === preferredServerId) - Number(a.id === preferredServerId));
    const probes = await Promise.allSettled(servers.map(server => request(server.id)));
    const answers: ServerAnswer<T>[] = [];
    const failedServerIds: string[] = [];
    probes.forEach((probe, i) => {
        if (probe.status === 'fulfilled') answers.push({ server: servers[i], data: probe.value });
        else if (getResponseStatus(probe.reason) !== 403) failedServerIds.push(servers[i].id);
    });
    return { answers, failedServerIds };
}

// One entry per podcast, keyed by its feed. The copy kept is the one on the
// preferred server, else the first server in the list; a server too old to
// send the feed address keeps its subscriptions apart.
function mergeSubscriptions(answers: ServerAnswer<PodcastSubscriptionVM[]>[]): ServerPodcastSubscription[] {
    const merged = new Map<string, ServerPodcastSubscription>();
    for (const { server, data } of answers) {
        for (const sub of data) {
            const key = sub.feedUrl?.trim().toLowerCase() || `${server.id}/${sub.id}`;
            const copy: PodcastSubscriptionCopy = { serverId: server.id, serverName: server.name, subscriptionId: sub.id };
            const existing = merged.get(key);
            if (existing) existing.subscribedOn.push(copy);
            else merged.set(key, { ...sub, serverId: server.id, serverName: server.name, subscribedOn: [copy] });
        }
    }
    return [...merged.values()].sort((a, b) => serverTimeMs(b.subscribedAt) - serverTimeMs(a.subscribedAt));
}

export const podcastService = {
    getSubscriptions: async (serverId?: string): Promise<PodcastSubscriptionVM[]> => {
        const response = await apiClient.get<PodcastSubscriptionVM[]>('/podcasts/subscriptions', { serverId });
        return response.data;
    },

    subscribe: async (feedUrl: string, serverId?: string): Promise<PodcastSubscriptionVM> => {
        const response = await apiClient.post<PodcastSubscriptionVM>('/podcasts/subscriptions', { feedUrl }, { serverId });
        return response.data;
    },

    unsubscribe: async (subscriptionId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/podcasts/subscriptions/${subscriptionId}`, { serverId });
    },

    refreshSubscription: async (subscriptionId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/podcasts/subscriptions/${subscriptionId}/refresh`, {}, { serverId });
    },

    getEpisodes: async (subscriptionId: string, limit?: number, serverId?: string): Promise<PodcastEpisodeVM[]> => {
        const response = await apiClient.get<PodcastEpisodeVM[]>(`/podcasts/subscriptions/${subscriptionId}/episodes`, {
            params: limit ? { limit } : undefined,
            serverId
        });
        return response.data;
    },

    saveEpisodeState: async (episodeId: string, positionSeconds: number, isPlayed?: boolean, serverId?: string): Promise<void> => {
        await apiClient.post(`/podcasts/episodes/${episodeId}/state`, { positionSeconds, isPlayed }, { serverId });
    },

    search: async (query: string, limit?: number, serverId?: string): Promise<DiscoveredPodcastVM[]> => {
        const response = await apiClient.get<DiscoveredPodcastVM[]>('/podcasts/search', {
            params: { q: query, ...(limit ? { limit } : {}) },
            serverId
        });
        return response.data;
    },

    getRecentEpisodes: async (limit?: number, days?: number, serverId?: string): Promise<PodcastFeedEpisodeVM[]> => {
        const params: Record<string, number> = {};
        if (limit) params.limit = limit;
        if (days) params.days = days;
        const response = await apiClient.get<PodcastFeedEpisodeVM[]>('/podcasts/episodes/recent', {
            params: Object.keys(params).length > 0 ? params : undefined,
            serverId
        });
        return response.data;
    },

    getSubscriptionsOnEveryServer: async (preferredServerId?: string): Promise<EveryServerResult<ServerPodcastSubscription>> => {
        const { answers, failedServerIds } = await askEveryServer(serverId => podcastService.getSubscriptions(serverId), preferredServerId);
        return { items: mergeSubscriptions(answers), failedServerIds };
    },

    getRecentEpisodesOnEveryServer: async (limit: number, days: number, preferredServerId?: string): Promise<EveryServerResult<ServerPodcastFeedEpisode>> => {
        const { answers, failedServerIds } = await askEveryServer(async serverId => {
            const [subscriptions, episodes] = await Promise.all([
                podcastService.getSubscriptions(serverId),
                podcastService.getRecentEpisodes(limit, days, serverId)
            ]);
            return { subscriptions, episodes };
        }, preferredServerId);
        const listed = new Set(mergeSubscriptions(answers.map(({ server, data }) => ({ server, data: data.subscriptions })))
            .map(sub => `${sub.serverId}/${sub.id}`));
        const items = answers
            .flatMap(({ server, data }) => data.episodes
                .filter(ep => listed.has(`${server.id}/${ep.subscriptionId}`))
                .map(ep => ({ ...ep, serverId: server.id })))
            .sort((a, b) => serverTimeMs(b.publishedAt) - serverTimeMs(a.publishedAt))
            .slice(0, limit);
        return { items, failedServerIds };
    },

    getCatalog: async (serverId?: string): Promise<CatalogPodcastVM[]> => {
        const response = await apiClient.get<CatalogPodcastVM[]>('/podcasts/catalog', { serverId });
        return response.data;
    },

    getAggregatedCatalog: async (): Promise<AggregatedCatalogResult> => {
        const { answers, failedServerIds } = await askEveryServer(serverId => podcastService.getCatalog(serverId));
        const merged = new Map<string, AggregatedCatalogPodcastVM>();

        for (const { server, data } of answers) {
            for (const row of data) {
                const key = row.feedUrl.trim().toLowerCase();
                if (key.length === 0) continue;
                const existing = merged.get(key);
                const availability: CatalogServerAvailability = {
                    serverId: server.id,
                    serverName: server.name,
                    showId: row.showId,
                    isSubscribed: row.isSubscribed
                };
                if (existing) {
                    existing.availableOn.push(availability);
                    if (!existing.artworkUrl && row.artworkUrl) existing.artworkUrl = row.artworkUrl;
                    if (!existing.description && row.description) existing.description = row.description;
                    if (!existing.author && row.author) existing.author = row.author;
                    if (!existing.homepageUrl && row.homepageUrl) existing.homepageUrl = row.homepageUrl;
                } else {
                    merged.set(key, {
                        feedUrl: row.feedUrl,
                        title: row.title,
                        author: row.author,
                        description: row.description,
                        artworkUrl: row.artworkUrl,
                        homepageUrl: row.homepageUrl,
                        availableOn: [availability]
                    });
                }
            }
        }

        const items = Array.from(merged.values()).sort((a, b) => a.title.localeCompare(b.title));
        return { items, failedServerIds };
    },

    addToCatalog: async (feedUrl: string, serverId?: string): Promise<CatalogPodcastVM> => {
        const response = await apiClient.post<CatalogPodcastVM>('/podcasts/admin/catalog', { feedUrl }, { serverId });
        return response.data;
    },

    removeFromCatalog: async (showId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/podcasts/admin/catalog/${showId}`, { serverId });
    }
};
