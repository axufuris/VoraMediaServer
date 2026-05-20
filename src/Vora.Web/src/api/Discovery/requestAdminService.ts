import { apiClient } from '../client';

export interface RequestServerVM {
    id?: string;
    name: string;
    providerId: string;
    mediaType: string;
    hostname: string;
    port: number;
    useSsl: boolean;
    apiKey: string;
    urlBase: string;
    isDefault: boolean;
    is4K: boolean;
    providerSettingsJson: string;
    isEnabled: boolean;
}

export interface ProviderOptionDto {
    id: string;
    name: string;
}

export interface ProviderOptionsRequestDto {
    providerId: string;
    optionType: string;
    hostname: string;
    port: number;
    useSsl: boolean;
    apiKey: string;
    urlBase: string;
}

export interface MediaRequestUserVM {
    profileId: string;
    requestedAt: string;
    profile: {
        name: string;
        profileImageUrl?: string;
    };
}

export interface MediaRequestVM {
    id: string;
    providerId: string;
    externalId: string;
    type: string;
    title: string;
    posterUrl?: string;
    status: number; // 0=Pending, 1=Approved, 2=Denied, 3=Processing, 4=Available
    createdAt: string;
    requesters: MediaRequestUserVM[];
}

export const requestAdminService = {
    getServers: async (serverId?: string): Promise<RequestServerVM[]> => {
        const response = await apiClient.get<RequestServerVM[]>('/requests/servers', { serverId });
        return response.data;
    },

    saveServer: async (server: RequestServerVM, serverId?: string): Promise<void> => {
        if (server.id && server.id !== '00000000-0000-0000-0000-000000000000') {
            await apiClient.put(`/requests/servers/${server.id}`, server, { serverId });
        } else {
            await apiClient.post('/requests/servers', server, { serverId });
        }
    },

    deleteServer: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/requests/servers/${id}`, { serverId });
    },

    getRequests: async (serverId?: string): Promise<MediaRequestVM[]> => {
        const response = await apiClient.get<MediaRequestVM[]>('/requests', { serverId });
        return response.data;
    },

    approveRequest: async (requestId: string, profileId?: number, serverId?: string): Promise<void> => {
        let url = `/requests/${requestId}/approve`;
        if (profileId) url += `?profileId=${profileId}`;
        await apiClient.put(url, null, { serverId });
    },

    deleteRequest: async (requestId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/requests/${requestId}`, { serverId });
    },

    getProviderOptions: async (request: ProviderOptionsRequestDto, serverId?: string): Promise<ProviderOptionDto[]> => {
        const response = await apiClient.post<ProviderOptionDto[]>('/requests/servers/options', request, { serverId });
        return response.data;
    }
};