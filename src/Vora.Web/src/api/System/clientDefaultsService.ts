import { apiClient } from '../client';
import type { ClientSettingsVM, DeviceSettingsVM, SaveClientSettingsRequest } from '../Users/profileService';

export interface ClientDefaultsVM extends ClientSettingsVM {
    device: DeviceSettingsVM;
}

export interface SaveClientDefaultsRequest extends SaveClientSettingsRequest {
    device?: DeviceSettingsVM;
}

export const clientDefaultsService = {
    get: async (serverId?: string): Promise<ClientDefaultsVM> => {
        const response = await apiClient.get<ClientDefaultsVM>('/settings/client-defaults', { serverId });
        return response.data;
    },
    save: async (request: SaveClientDefaultsRequest, serverId?: string): Promise<ClientDefaultsVM> => {
        const response = await apiClient.put<ClientDefaultsVM, SaveClientDefaultsRequest>('/settings/client-defaults', request, { serverId });
        return response.data;
    },
};
