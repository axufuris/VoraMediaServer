import { apiClient, createDirectClient } from '../client';
import type { AuthResponse } from './authService';

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

export interface DevicePairingStart {
    deviceCode: string;
    userCode: string;
    verificationUrl: string;
    verificationUrlComplete: string;
    expiresInSeconds: number;
    intervalSeconds: number;
}

export type DevicePairingPollStatus = 'pending' | 'slow_down' | 'approved' | 'denied' | 'expired';

export interface DevicePairingPoll {
    status: DevicePairingPollStatus;
    intervalSeconds?: number | null;
    auth?: (AuthResponse & { refreshToken?: string | null }) | null;
}

export const devicePairingService = {
    start: async (baseUrl: string): Promise<DevicePairingStart> => {
        const response = await createDirectClient(baseUrl).post<DevicePairingStart>('/auth/device/start');
        return response.data;
    },

    poll: async (baseUrl: string, deviceCode: string): Promise<DevicePairingPoll> => {
        const response = await createDirectClient(baseUrl).post<DevicePairingPoll>('/auth/device/poll', { deviceCode });
        return response.data;
    },

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
