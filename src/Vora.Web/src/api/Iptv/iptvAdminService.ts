import { apiClient } from '../client';

export type IptvChannelKind = 'Tv' | 'Radio';

export interface IptvChannelVM {
    id: string;
    playlistId: string;
    externalChannelId: string;
    name: string;
    logoUrl?: string;
    groupTitle?: string;
    streamUrl: string;
    resolution?: string;
    countryCode?: string;
    isHiddenByAdmin: boolean;
    kind: IptvChannelKind;
}

export interface IptvPlaylistVM {
    id: string;
    name: string;
    m3uUrl?: string;
    isActive: boolean;
    supportsWebPlayback: boolean;
    maxConcurrentStreams: number;
    lastError?: string;
    lastSyncedAt?: string;
    defaultChannelKind: IptvChannelKind;
    channels?: IptvChannelVM[];
}

export const iptvAdminService = {
    getPlaylists: async (serverId?: string, kind?: IptvChannelKind): Promise<IptvPlaylistVM[]> => {
        const response = await apiClient.get<IptvPlaylistVM[]>('/iptv/admin/playlists', {
            params: kind ? { kind } : undefined,
            serverId
        });
        return response.data;
    },

    addPlaylist: async (
        name: string,
        m3uUrl: string,
        supportsWebPlayback: boolean,
        maxConcurrentStreams: number,
        defaultChannelKind: IptvChannelKind,
        serverId?: string
    ): Promise<IptvPlaylistVM> => {
        const response = await apiClient.post<IptvPlaylistVM>(
            '/iptv/admin/playlists',
            { name, m3uUrl, supportsWebPlayback, maxConcurrentStreams, defaultChannelKind },
            { serverId }
        );
        return response.data;
    },

    updatePlaylist: async (
        id: string,
        name: string,
        m3uUrl: string,
        supportsWebPlayback: boolean,
        maxConcurrentStreams: number,
        isActive: boolean,
        defaultChannelKind: IptvChannelKind,
        serverId?: string
    ): Promise<IptvPlaylistVM> => {
        const response = await apiClient.put<IptvPlaylistVM>(
            `/iptv/admin/playlists/${id}`,
            { name, m3uUrl, supportsWebPlayback, maxConcurrentStreams, isActive, defaultChannelKind },
            { serverId }
        );
        return response.data;
    },

    deletePlaylist: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/iptv/admin/playlists/${id}`, { serverId });
    },

    refreshPlaylist: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/iptv/admin/playlists/${id}/refresh`, {}, { serverId });
    },

    toggleChannelVisibility: async (channelId: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/iptv/admin/channels/${channelId}/toggle-visibility`, {}, { serverId });
    },

    setChannelKind: async (channelId: string, kind: IptvChannelKind, serverId?: string): Promise<void> => {
        await apiClient.put(`/iptv/admin/channels/${channelId}/kind`, { kind }, { serverId });
    }
};
