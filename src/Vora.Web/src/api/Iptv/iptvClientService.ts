import { apiClient } from '../client';
import { type IptvPlaylistVM } from './iptvAdminService';

export interface IptvProgramDto {
    id: string;
    channelId: string;
    title: string;
    description?: string;
    startTime: string;
    endTime: string;
    contentRating: string;
}

export const iptvClientService = {
    getPlaylists: async (userId: string, profileId?: string, serverId?: string): Promise<IptvPlaylistVM[]> => {
        const response = await apiClient.get<IptvPlaylistVM[]>(`/iptv/client/playlists/${userId}`, {
            params: { profileId },
            serverId
        });
        return response.data;
    },

    getGuide: async (
        userId: string,
        profileId: string,
        channelIds: string[],
        startTime: string,
        endTime: string,
        serverId?: string
    ): Promise<Record<string, IptvProgramDto[]>> => {
        const response = await apiClient.post<Record<string, IptvProgramDto[]>>('/iptv/guide', {
            userId,
            profileId,
            channelIds,
            startTime,
            endTime
        }, { serverId });

        return response.data;
    }
};
