import { apiClient } from '../client';
import type { ThemeManifest } from '../../theme/types';

export interface ThemeMetaVM {
    id: string;
    name: string;
    version: string;
    author?: string;
    description?: string;
    preview?: string;
    isBuiltIn: boolean;
}

export interface ActiveThemeResponse {
    themeId: string;
}

export const themeService = {

    getAll: async (serverId?: string): Promise<ThemeMetaVM[]> => {
        const response = await apiClient.get<ThemeMetaVM[]>('/admin/themes', { serverId });
        return response.data;
    },

    getActiveId: async (serverId?: string): Promise<string> => {
        const response = await apiClient.get<ActiveThemeResponse>('/admin/themes/active', { serverId });
        return response.data.themeId;
    },

    setActiveId: async (themeId: string, serverId?: string): Promise<void> => {
        await apiClient.put('/admin/themes/active', { themeId }, { serverId });
    },

    getManifest: async (themeId: string, serverId?: string): Promise<ThemeManifest> => {
        const response = await apiClient.get<ThemeManifest>(`/admin/themes/${encodeURIComponent(themeId)}/manifest`, { serverId });
        return response.data;
    },

    rescan: async (serverId?: string): Promise<number> => {
        const response = await apiClient.post<{ bundleCount: number }>('/admin/themes/rescan', undefined, { serverId });
        return response.data.bundleCount;
    },
};
