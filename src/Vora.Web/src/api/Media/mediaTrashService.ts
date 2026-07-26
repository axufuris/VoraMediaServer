import { apiClient } from '../client';

export interface TrashMediaItem {
    id: string;
    title: string;
    type: string;
    posterUrl?: string;
    libraryName?: string;
    seriesTitle?: string;
    missingSince: string;
}

export const mediaTrashService = {
    getTrash: async (serverId?: string): Promise<TrashMediaItem[]> => {
        const response = await apiClient.get<TrashMediaItem[]>('/media/trash', { serverId });
        return response.data;
    },

    restore: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/trash/${id}/restore`, null, { serverId });
    },

    purge: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/media/trash/${id}`, { serverId });
    },
};
