import { apiClient } from '../client';

// The watchlist spans library items and titles that aren't in the library, so
// it lives outside the Discovery API — those endpoints are gated on the Discover
// feature and would 403 on a server with Discover switched off.
export interface WatchlistItem {
    id: string;
    profileId: string;
    externalId: string;
    providerId: string;
    type: string;
    title: string;
    posterUrl?: string;
    addedAt: string;
    // Present when the title is in the library, so the tile opens the local item
    // rather than the provider page.
    mediaItemId?: string;
}

// Identify the title either by the library item it is, or by the external
// provider entry it came from. The server reconciles the two so the same title
// is one entry however it was added.
export type WatchlistTarget =
    | { mediaItemId: string }
    | { externalId: string; providerId: string };

interface ToggleBody extends Partial<{ mediaItemId: string; externalId: string; providerId: string }> {
    type: string;
    title: string;
    posterUrl?: string;
    expectedReleaseDate?: string;
}

export const watchlistService = {
    getWatchlist: async (serverId?: string): Promise<WatchlistItem[]> => {
        const response = await apiClient.get<WatchlistItem[]>('/watchlist', { serverId });
        return response.data;
    },

    check: async (target: WatchlistTarget, serverId?: string): Promise<boolean> => {
        const params = new URLSearchParams(
            'mediaItemId' in target
                ? { mediaItemId: target.mediaItemId }
                : { externalId: target.externalId, providerId: target.providerId }
        );
        const response = await apiClient.get<{ inWatchlist: boolean }>(`/watchlist/check?${params}`, { serverId });
        return response.data.inWatchlist;
    },

    // Returns the state the title is in after the toggle.
    toggle: async (
        target: WatchlistTarget,
        details: { type: string; title: string; posterUrl?: string; expectedReleaseDate?: string },
        serverId?: string
    ): Promise<boolean> => {
        const body: ToggleBody = { ...target, ...details };
        const response = await apiClient.post<{ inWatchlist: boolean }>('/watchlist/toggle', body, { serverId });
        return response.data.inWatchlist;
    },
};
