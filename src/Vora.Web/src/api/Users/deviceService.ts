import { apiClient } from '../client';

export interface ClientDeviceVM {
    id: string;
    deviceId: string;
    clientName: string;
    deviceName: string;
    deviceType: string;
    lastIpAddress: string;
    operatingSystem: string;
    location: string;
    lastUserId?: string;
    lastProfileId?: string;
    lastConnectedAt: string;
    isBlocked: boolean;
}

export interface DeviceCapabilitiesDto {
    videoCodecs: string[];
    audioCodecs: string[];
    containers: string[];
    maxAudioChannels: number;
    supportedHdrFormats: string[];
    maxVideoBitDepth: number;
}

export const deviceService = {
    getDevices: async (serverId?: string): Promise<ClientDeviceVM[]> => {
        const response = await apiClient.get<ClientDeviceVM[]>('/admin/devices', { serverId });
        return response.data;
    },

    blockDevice: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/admin/devices/${id}/block`, null, { serverId });
    },

    unblockDevice: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/admin/devices/${id}/unblock`, null, { serverId });
    },

    updateCapabilities: async (capabilities: DeviceCapabilitiesDto, serverId?: string): Promise<void> => {
        await apiClient.put('/devices/capabilities', capabilities, { serverId });
    },

    deleteDevice: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/devices/${id}`, { serverId });
    }
};