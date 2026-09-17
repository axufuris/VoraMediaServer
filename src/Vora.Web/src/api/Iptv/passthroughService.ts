import { apiClient } from '../client';

export type PassthroughStreamType = 'hls' | 'audio';

export interface PassthroughStartResult {
    url: string;
    streamType: PassthroughStreamType;
}

export const passthroughService = {
    startPassthrough: async (channelId: string, serverId?: string): Promise<PassthroughStartResult> => {
        const response = await apiClient.post<PassthroughStartResult>('/iptv/passthrough/start', { channelId }, { serverId });
        return response.data;
    }
};
