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
    // What the plugin calls itself in results it returns — "Fanart.tv" where
    // name is "Fanart.tv Music Artwork". Match results on this, not on name.
    providerName: string;
    externalIdLabel: string;
    externalIdPlaceholder: string;
    isAiPlugin: boolean;
    supportedLibraryTypes: string[];
}

// libraryType narrows a provider list to the plugins that declare support for
// that kind, so a film's picker does not offer the music providers.
//
// The filtering itself is deliberately NOT done here. Every caller sends the kind
// and the server applies one rule, because the rule previously existed in three
// places — this file's callers filtered client-side, twice, with different type
// derivations and different case sensitivity — and three copies of a rule is
// three chances for it to drift.
const optionsQuery = (type: string, libraryType?: string) =>
    libraryType
        ? `?type=${encodeURIComponent(type)}&libraryType=${encodeURIComponent(libraryType)}`
        : `?type=${encodeURIComponent(type)}`;

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

    getMetadataProviders: async (serverId?: string, libraryType?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>(`/plugins/options${optionsQuery('Metadata', libraryType)}`, { serverId });
        return response.data;
    },

    getRatingsProviders: async (serverId?: string, libraryType?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>(`/plugins/options${optionsQuery('Ratings', libraryType)}`, { serverId });
        return response.data;
    },

    getArtworkProviders: async (serverId?: string, libraryType?: string): Promise<PluginOptionVM[]> => {
        const response = await apiClient.get<PluginOptionVM[]>(`/plugins/options${optionsQuery('Artwork', libraryType)}`, { serverId });
        return response.data;
    },

    uninstallPlugin: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/plugins/${id}`, { serverId });
    }
};