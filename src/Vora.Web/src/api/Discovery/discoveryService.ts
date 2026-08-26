import { apiClient } from '../client';

export interface Showtime {
    time: string;
    format: string;
    ticketUrl?: string;
}

export interface Theater {
    name: string;
    address: string;
    showtimes: Showtime[];
}

export interface DiscoveryRowConfig {
    id: string;
    rowId: string;
    providerId: string;
    name: string;
    providerName?: string;
    orderIndex: number;
    isEnabled: boolean;
}

export type DiscoveryRequestStatus = 'Pending' | 'Approved' | 'Denied' | 'Processing' | 'Available';

export interface DiscoveryItem {
    externalId: string;
    providerId: string;
    title: string;
    type: string;
    year?: number;
    releaseDate?: string;
    posterUrl?: string;
    contentRating?: string;
    inLibrary?: boolean;
    requestStatus?: DiscoveryRequestStatus | null;
}

export interface CastMember {
    externalId: string;
    name: string;
    role: string;
    profileImageUrl?: string;
}

export interface Trailer {
    name: string;
    url: string;
}

export interface DiscoveryActor {
    externalId: string;
    providerId: string;
    name: string;
    biography?: string;
    placeOfBirth?: string;
    birthday?: string;
    deathday?: string;
    profileImageUrl?: string;
    filmography: DiscoveryItem[];
}

export interface DiscoveryItemDetails extends DiscoveryItem {
    overview?: string;
    backgroundUrl?: string;
    nextAirDate?: string;
    runtimeMinutes?: number;
    rating?: number;
    genres?: string[];
    studios?: string[];
    cast: CastMember[];
    trailers: Trailer[];
}

export interface WatchlistItem {
    id: string;
    profileId: string;
    externalId: string;
    providerId: string;
    type: string;
    title: string;
    posterUrl?: string;
    addedAt: string;
}

export const discoveryService = {
    getAdminConfigs: async (serverId?: string): Promise<DiscoveryRowConfig[]> => {
        const response = await apiClient.get<DiscoveryRowConfig[]>('/discovery/config', { serverId });
        return response.data;
    },
    updateAdminConfigs: async (configs: DiscoveryRowConfig[], serverId?: string): Promise<void> => {
        await apiClient.put('/discovery/config', configs, { serverId });
    },

    getRowItems: async (providerId: string, rowId: string, page: number = 1, serverId?: string): Promise<DiscoveryItem[]> => {
        const response = await apiClient.get<DiscoveryItem[]>(`/discovery/rows/${providerId}/${rowId}/items?page=${page}`, { serverId });
        return response.data;
    },
    getItemDetails: async (providerId: string, type: string, externalId: string, serverId?: string): Promise<DiscoveryItemDetails> => {
        const response = await apiClient.get<DiscoveryItemDetails>(`/discovery/details/${providerId}/${type}/${externalId}`, { serverId });
        return response.data;
    },
    getActorDetails: async (providerId: string, externalId: string, serverId?: string): Promise<DiscoveryActor> => {
        const response = await apiClient.get<DiscoveryActor>(`/discovery/actor/${providerId}/${externalId}`, { serverId });
        return response.data;
    },
    getShowtimes: async (movieTitle: string, location: string, maxTheaters?: number, serverId?: string): Promise<Theater[]> => {
        const maxParam = maxTheaters ? `&maxTheaters=${maxTheaters}` : '';
        const response = await apiClient.get<Theater[]>(`/discovery/theater/showtimes?movieTitle=${encodeURIComponent(movieTitle)}&location=${encodeURIComponent(location)}${maxParam}`, { serverId });
        return response.data;
    },

    getTheaterAutoLoad: async (serverId?: string): Promise<boolean> => {
        const response = await apiClient.get<boolean>(`/discovery/theater/auto-load`, { serverId });
        return response.data;
    },

    getWatchlist: async (profileId: string, serverId?: string): Promise<WatchlistItem[]> => {
        const response = await apiClient.get<WatchlistItem[]>(`/discovery/profiles/${profileId}/watchlist`, { serverId });
        return response.data;
    },
    checkWatchlist: async (profileId: string, externalId: string, providerId: string, serverId?: string): Promise<boolean> => {
        const response = await apiClient.get<{ inWatchlist: boolean }>(`/discovery/profiles/${profileId}/watchlist/check?externalId=${externalId}&providerId=${providerId}`, { serverId });
        return response.data.inWatchlist;
    },
    toggleWatchlist: async (profileId: string, externalId: string, providerId: string, type: string, title: string, posterUrl?: string, expectedReleaseDate?: string, serverId?: string): Promise<void> => {
        await apiClient.post(
            `/discovery/profiles/${profileId}/watchlist/toggle`,
            { externalId, providerId, type, title, posterUrl, expectedReleaseDate },
            { serverId }
        );
    },
    search: async (query: string, serverId?: string): Promise<DiscoveryItem[]> => {
        const response = await apiClient.get<DiscoveryItem[]>(`/discovery/search?q=${encodeURIComponent(query)}`, { serverId });
        return response.data;
    },
    getRequestStatus: async (externalId: string, type: string, serverId?: string): Promise<number> => {
        const response = await apiClient.get<{ status: number }>(`/requests/status?externalId=${externalId}&type=${type}`, { serverId });
        return response.data.status;
    },
};