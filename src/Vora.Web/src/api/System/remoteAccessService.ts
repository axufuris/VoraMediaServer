import { apiClient } from '../client';

export interface RemoteAccessStatus {
    isEnabled: boolean;
    upnpSupported: boolean;
    localIp: string;
    localPort: number;
    publicIp: string;
    publicPort: number;
    manuallySpecifyPort: boolean;
    externalUrl?: string | null;
    reachable: boolean;
    accessUrl: string;
    errorMessage: string;
}

export interface UpdateRemoteAccessRequest {
    isEnabled: boolean;
    manuallySpecifyPort: boolean;
    publicPort: number;
    externalUrl?: string | null;
}

export const remoteAccessService = {
    getRemoteAccessStatus: async (serverId?: string): Promise<RemoteAccessStatus> => {
        const response = await apiClient.get<RemoteAccessStatus>('/remote-access', { serverId });
        return response.data;
    },
    updateRemoteAccess: async (request: UpdateRemoteAccessRequest, serverId?: string): Promise<RemoteAccessStatus> => {
        const response = await apiClient.put<RemoteAccessStatus>('/remote-access', request, { serverId });
        return response.data;
    }
};
