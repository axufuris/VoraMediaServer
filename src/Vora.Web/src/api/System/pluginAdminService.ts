import { apiClient } from '../client';

export interface PluginVM {
    id: string;
    name: string;
    version: string;
    description: string;
    isSystemPlugin: boolean;
    type: string;
    hasSettings: boolean;
    developerName?: string;
    latestVersionApiUrl?: string;
    documentationUrl?: string;
    externalConfigurationHint?: string;
    isAiPlugin: boolean;
    isEnabled: boolean;
    requiresConfiguration: boolean;
    supportsConnectionTest: boolean;
}

export interface PluginOptionVM {
    id: string;
    name: string;
    externalIdLabel: string;
    externalIdPlaceholder: string;
    isAiPlugin: boolean;
    supportedLibraryTypes: string[];
}

export const pluginAdminService = {
    getPlugins: async (serverId?: string): Promise<PluginVM[]> => {
        const response = await apiClient.get<PluginVM[]>('/plugins', { serverId });
        return response.data;
    },

    uploadPlugin: async (file: File, serverId?: string): Promise<void> => {
        const formData = new FormData();
        formData.append('file', file);
        await apiClient.post('/plugins/upload', formData, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
    },

    getChronologyProviders: async (serverId?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>('/plugins/options?type=Chronology', { serverId });
        return response.data;
    },

    getCollectionSyncProviders: async (serverId?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>('/plugins/options?type=Collection_Sync', { serverId });
        return response.data;
    },

    getMetadataProviders: async (serverId?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>('/plugins/options?type=Metadata', { serverId });
        return response.data;
    },

    getRatingsProviders: async (serverId?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>('/plugins/options?type=Ratings', { serverId });
        return response.data;
    },

    // libraryType narrows the list to plugins that declare support for that kind,
    // so a film's artwork picker does not offer the music providers. Trailing and
    // optional, so callers that genuinely want every provider are unaffected.
    getArtworkProviders: async (serverId?: string, libraryType?: string): Promise<PluginOptionVM[]> => {
        const query = libraryType ? `?type=Artwork&libraryType=${encodeURIComponent(libraryType)}` : '?type=Artwork';
        const response = await apiClient.get<PluginOptionVM[]>(`/plugins/options${query}`, { serverId });
        return response.data;
    },

    uninstallPlugin: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/plugins/${id}`, { serverId });
    }
};