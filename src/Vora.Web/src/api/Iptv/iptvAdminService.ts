import { apiClient } from '../client';

export type IptvChannelKind = 'Tv' | 'Radio';

export type IptvSourceType = 'M3u' | 'Xtream' | 'HdHomeRun';

export interface IptvChannelVM {
    id: string;
    playlistId: string;
    playlistName?: string;
    externalChannelId: string;
    name: string;
    logoUrl?: string;
    groupTitle?: string;
    streamUrl: string;
    resolution?: string;
    countryCode?: string;
    isHiddenByAdmin: boolean;
    isHealthy?: boolean | null;
    lastHealthCheckAt?: string | null;
    kind: IptvChannelKind;
}

export interface IptvPlaylistVM {
    id: string;
    name: string;
    sourceType?: IptvSourceType;
    m3uUrl?: string;
    serverUrl?: string | null;
    username?: string | null;
    passwordIsSet?: boolean;
    userAgent?: string | null;
    referer?: string | null;
    useProviderGuide?: boolean;
    accountStatus?: string | null;
    accountExpiresAt?: string | null;
    accountMaxConnections?: number | null;
    accountCheckedAt?: string | null;
    isActive: boolean;
    supportsWebPlayback: boolean;
    maxConcurrentStreams: number;
    connectionLimit?: number;
    lastError?: string;
    lastSyncedAt?: string;
    defaultChannelKind: IptvChannelKind;
    countryFilter?: string | null;
    enableHealthCheck?: boolean;
    channels?: IptvChannelVM[];
}

export interface IptvPlaylistRequest {
    name: string;
    sourceType: IptvSourceType;
    m3uUrl?: string | null;
    serverUrl?: string | null;
    username?: string | null;
    newPassword?: string | null;
    clearPassword?: boolean;
    userAgent?: string | null;
    referer?: string | null;
    useProviderGuide?: boolean;
    supportsWebPlayback: boolean;
    maxConcurrentStreams: number;
    isActive?: boolean;
    defaultChannelKind: IptvChannelKind;
    countryFilter?: string | null;
    enableHealthCheck: boolean;
}

export interface IptvConnectionTestVM {
    succeeded: boolean;
    error?: string | null;
    accountStatus?: string | null;
    accountExpiresAt?: string | null;
    accountMaxConnections?: number | null;
    channelCount: number;
}

export interface HdHomeRunTunerVM {
    index: number;
    inUse: boolean;
    channelNumber?: string | null;
    channelName?: string | null;
    frequencyMhz?: number | null;
    signalStrengthPercent?: number | null;
    signalQualityPercent?: number | null;
    symbolQualityPercent?: number | null;
    bitsPerSecond?: number | null;
    targetAddress?: string | null;
}

export interface HdHomeRunStatusVM {
    succeeded: boolean;
    error?: string | null;
    deviceName?: string | null;
    model?: string | null;
    firmwareVersion?: string | null;
    deviceId?: string | null;
    tunerCount: number;
    tunersInUse: number;
    voraStreams: number;
    tuners: HdHomeRunTunerVM[];
}

export const iptvAdminService = {
    getPlaylists: async (serverId?: string, kind?: IptvChannelKind): Promise<IptvPlaylistVM[]> => {
        const response = await apiClient.get<IptvPlaylistVM[]>('/iptv/admin/playlists', {
            params: kind ? { kind } : undefined,
            serverId
        });
        return response.data;
    },

    addPlaylist: async (request: IptvPlaylistRequest, serverId?: string): Promise<IptvPlaylistVM> => {
        const response = await apiClient.post<IptvPlaylistVM>('/iptv/admin/playlists', request, { serverId });
        return response.data;
    },

    updatePlaylist: async (id: string, request: IptvPlaylistRequest, serverId?: string): Promise<IptvPlaylistVM> => {
        const response = await apiClient.put<IptvPlaylistVM>(`/iptv/admin/playlists/${id}`, request, { serverId });
        return response.data;
    },

    testConnection: async (request: IptvPlaylistRequest, playlistId?: string, serverId?: string): Promise<IptvConnectionTestVM> => {
        const response = await apiClient.post<IptvConnectionTestVM>('/iptv/admin/playlists/test-connection', { ...request, playlistId }, { serverId });
        return response.data;
    },

    getTunerStatus: async (id: string, serverId?: string): Promise<HdHomeRunStatusVM> => {
        const response = await apiClient.get<HdHomeRunStatusVM>(`/iptv/admin/playlists/${id}/tuners`, { serverId });
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
