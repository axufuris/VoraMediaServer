import { apiClient } from '../client';

export interface ServerVersionVM {
    version: string;
    commit?: string;
    isPrerelease: boolean;
}

export const versionService = {
    getVersion: async (serverId?: string): Promise<ServerVersionVM> => {
        const response = await apiClient.get<ServerVersionVM>('/system/version', { serverId });
        return response.data;
    },
};
