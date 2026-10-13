import { apiClient } from '../client';

export interface AiStatus {
    // The saved connection can answer a chat request.
    configured: boolean;
    // It also has an embedding model, which recommendations and AI playlists need.
    embeddings: boolean;
    isOpenAi: boolean;
    host: string;
}

export interface AiModels {
    chatModels: string[];
    embeddingModels: string[];
    // Why the server could not be asked, in words for the admin.
    error?: string | null;
}

export const aiSettingsService = {
    getStatus: async (serverId?: string): Promise<AiStatus> => {
        const response = await apiClient.get<AiStatus>('/settings/ai/status', { serverId });
        return response.data;
    },

    // Asks the server at the address in the form, not the saved one, so the
    // model lists follow what is being typed.
    listModels: async (settings: Record<string, string>, serverId?: string): Promise<AiModels> => {
        const response = await apiClient.post<AiModels>('/settings/ai/models', settings, { serverId });
        return response.data;
    },
};
