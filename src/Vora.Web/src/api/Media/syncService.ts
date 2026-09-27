import { apiClient } from '../client';

export interface ContinueWatchingItem {
    id: string;
    title: string;
    sortTitle?: string;
    type: string;
    posterUrl?: string;
    backgroundUrl?: string;
    releaseDate?: string;
    resumePositionSeconds?: number;
    durationSeconds?: number;
    tvShowId?: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
}

export const syncService = {
    getContinueWatching: async (profileId: string, serverId?: string): Promise<ContinueWatchingItem[]> => {
        const response = await apiClient.get<ContinueWatchingItem[]>(`/sync/profiles/${profileId}/continue-watching`, { serverId });
        return response.data;
    },

    hideFromContinueWatching: async (profileId: string, mediaItemId: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/sync/profiles/${profileId}/continue-watching/${mediaItemId}/hide`, null, { serverId });
    }
};
