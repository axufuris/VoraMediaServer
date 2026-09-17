import { apiClient } from '../client';

export interface IptvEpgSourceVM {
    id: string;
    name: string;
    xmlTvUrl: string;
    priority: number;
    isActive: boolean;
    lastError?: string;
    lastSyncedAt?: string;
}

export interface DbChannelSample {
    externalChannelId: string;
    name: string;
    playlistName: string;
}

export interface EpgSourceDiagnostics {
    sourceId: string;
    name: string;
    xmlTvUrl: string;
    totalProgrammes: number;
    matchedProgrammes: number;
    matchedChannels: number;
    matchRate: number;
    unmatchedSamples: string[];
    syncedAt?: string;
    lastError?: string;
}

export interface ChannelCoverageSummary {
    totalChannels: number;
    channelsWithEpg: number;
    coverageRate: number;
    uncoveredSamples: DbChannelSample[];
}

export interface IptvEpgDiagnosticsVM {
    dbSampleIds: DbChannelSample[];
    sources: EpgSourceDiagnostics[];
    coverage: ChannelCoverageSummary;
}

export const iptvEpgAdminService = {
    getSources: async (serverId?: string): Promise<IptvEpgSourceVM[]> => {
        const response = await apiClient.get<IptvEpgSourceVM[]>('/iptv/admin/epg-sources', { serverId });
        return response.data;
    },

    addSource: async (
        name: string,
        xmlTvUrl: string,
        priority: number,
        serverId?: string
    ): Promise<IptvEpgSourceVM> => {
        const response = await apiClient.post<IptvEpgSourceVM>(
            '/iptv/admin/epg-sources',
            { name, xmlTvUrl, priority },
            { serverId }
        );
        return response.data;
    },

    updateSource: async (
        id: string,
        name: string,
        xmlTvUrl: string,
        priority: number,
        isActive: boolean,
        serverId?: string
    ): Promise<IptvEpgSourceVM> => {
        const response = await apiClient.put<IptvEpgSourceVM>(
            `/iptv/admin/epg-sources/${id}`,
            { name, xmlTvUrl, priority, isActive },
            { serverId }
        );
        return response.data;
    },

    deleteSource: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/iptv/admin/epg-sources/${id}`, { serverId });
    },

    refreshSource: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/iptv/admin/epg-sources/${id}/refresh`, {}, { serverId });
    },

    getDiagnostics: async (serverId?: string): Promise<IptvEpgDiagnosticsVM> => {
        const response = await apiClient.get<IptvEpgDiagnosticsVM>('/iptv/admin/epg-diagnostics', { serverId });
        return response.data;
    }
};
