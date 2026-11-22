import { apiClient } from '../client';
import type { LiveVideoLimits } from '../../utils/liveVideoLimits';

export type PassthroughStreamType = 'hls' | 'audio';

export interface PassthroughStartResult {
    url: string;
    streamType: PassthroughStreamType;
    audioFormat?: 'mp3' | 'aac' | 'hls' | null;
}

export const passthroughService = {
    // limits: what the player takes, sent for a TV channel. A radio start is the channel alone.
    startPassthrough: async (channelId: string, serverId?: string, limits?: LiveVideoLimits): Promise<PassthroughStartResult> => {
        const response = await apiClient.post<PassthroughStartResult>('/iptv/passthrough/start', { channelId, ...limits }, { serverId });
        return response.data;
    }
};
