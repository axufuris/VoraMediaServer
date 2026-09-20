import { apiClient } from '../client';
import { serverVault } from '../../utils/serverVault';

export interface PodcastSubscriptionVM {
    id: string;
    showId: string;
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

    getCatalog: async (serverId?: string): Promise<CatalogPodcastVM[]> => {
        const response = await apiClient.get<CatalogPodcastVM[]>('/podcasts/catalog', { serverId });
        return response.data;
    },

    getAggregatedCatalog: async (): Promise<AggregatedCatalogResult> => {
        const servers = serverVault.getServers();
        if (servers.length === 0) {
            return { items: [], failedServerIds: [] };
        }

        const probes = await Promise.allSettled(
            servers.map(s => apiClient.get<CatalogPodcastVM[]>('/podcasts/catalog', { serverId: s.id }).then(r => ({ server: s, rows: r.data })))
        );

        const merged = new Map<string, AggregatedCatalogPodcastVM>();
        const failedServerIds: string[] = [];

        for (let i = 0; i < probes.length; i++) {
            const probe = probes[i];
            const server = servers[i];
            if (probe.status === 'rejected') {
                failedServerIds.push(server.id);
                continue;
            }
            for (const row of probe.value.rows) {
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
