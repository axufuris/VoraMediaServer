import { apiClient } from '../client';

export interface DevicePairingRequest {
    userCode: string;
    clientName: string;
    deviceName: string;
    deviceType: string;
    operatingSystem: string;
    location: string;
    sameNetwork: boolean;
    requestedAt: string;
    expiresAt: string;
}

export interface DevicePairingDecision {
    status: 'approved' | 'denied';
}

export const devicePairingService = {
    lookup: async (userCode: string, serverId?: string): Promise<DevicePairingRequest> => {
        const response = await apiClient.get<DevicePairingRequest>('/auth/device/lookup', { params: { userCode }, serverId });
        return response.data;
    },

    approve: async (userCode: string, serverId?: string): Promise<DevicePairingDecision> => {
        const response = await apiClient.post<DevicePairingDecision>('/auth/device/approve', { userCode }, { serverId });
        return response.data;
    },

    deny: async (userCode: string, serverId?: string): Promise<DevicePairingDecision> => {
        const response = await apiClient.post<DevicePairingDecision>('/auth/device/deny', { userCode }, { serverId });
        return response.data;
    },
};
