import { apiClient } from '../client';

export const timeshiftService = {
    startTimeshift: async (channelId: string, serverId?: string): Promise<{ url: string }> => {
        const response = await apiClient.post<{ url: string }>('/iptv/timeshift/start', { channelId }, { serverId });
        return response.data;
    },

    stopTimeshift: async (serverId?: string): Promise<void> => {
        await apiClient.post('/iptv/timeshift/stop', {}, { serverId });
    },

    pingTimeshift: async (serverId?: string): Promise<void> => {
        await apiClient.post('/iptv/timeshift/ping', {}, { serverId });
    }
};
