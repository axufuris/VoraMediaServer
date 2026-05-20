import { apiClient } from '../client';

export const profileDeviceSettingsService = {
    getNavPrefs: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ navPrefsJson: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/nav`, { serverId });
        return response.data.navPrefsJson;
    },

    saveNavPrefs: async (profileId: string, deviceId: string, navPrefsJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/nav`, { navPrefsJson }, { serverId });
    },

    getPlaybackPrefs: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ playbackPrefs: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/playback`, { serverId });
        return response.data.playbackPrefs;
    },

    savePlaybackPrefs: async (profileId: string, deviceId: string, playbackPrefs: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/playback`, { playbackPrefs }, { serverId });
    },

    getDiscoveryLayout: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ discoveryLayoutJson: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/discovery-layout`, { serverId });
        return response.data.discoveryLayoutJson;
    },

    saveDiscoveryLayout: async (profileId: string, deviceId: string, discoveryLayoutJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/discovery-layout`, { discoveryLayoutJson }, { serverId });
    },

    getHomeLayout: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ homeLayoutJson: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/home-layout`, { serverId });
        return response.data.homeLayoutJson;
    },

    saveHomeLayout: async (profileId: string, deviceId: string, layoutJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/home-layout`, { layoutJson }, { serverId });
    },

    getIptvPrefs: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ iptvPrefsJson: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/iptv`, { serverId });
        return response.data.iptvPrefsJson;
    },

    saveIptvPrefs: async (profileId: string, deviceId: string, iptvPrefsJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/iptv`, { iptvPrefsJson }, { serverId });
    },

    getRadioPrefs: async (profileId: string, deviceId: string, serverId?: string): Promise<string | null> => {
        const response = await apiClient.get<{ radioPrefsJson: string | null }>(`/users/profiles/${profileId}/devices/${deviceId}/radio`, { serverId });
        return response.data.radioPrefsJson;
    },

    saveRadioPrefs: async (profileId: string, deviceId: string, radioPrefsJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/radio`, { radioPrefsJson }, { serverId });
    },

    saveClientSettings: async (profileId: string, deviceId: string, playbackPrefs: string, iptvPrefsJson: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/devices/${deviceId}/settings`, { playbackPrefs, iptvPrefsJson }, { serverId });
    }
};
