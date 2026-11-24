import { apiClient } from '../client';
import type { ClientSettingsVM, SaveClientSettingsRequest } from '../Users/profileService';

export const clientDefaultsService = {
    get: async (serverId?: string): Promise<ClientSettingsVM> => {
        const response = await apiClient.get<ClientSettingsVM>('/settings/client-defaults', { serverId });
        return response.data;
    },
    save: async (request: SaveClientSettingsRequest, serverId?: string): Promise<ClientSettingsVM> => {
        const response = await apiClient.put<ClientSettingsVM, SaveClientSettingsRequest>('/settings/client-defaults', request, { serverId });
        return response.data;
    },
};
