import { apiClient } from '../client';

export const dvrPlaybackService = {
    playDvrSession: async (sessionId: string, serverId?: string): Promise<{ url: string }> => {
        const response = await apiClient.post<{ url: string }>(`/streaming/dvr/play/${sessionId}`, {}, { serverId });
        return response.data;
    }
};
