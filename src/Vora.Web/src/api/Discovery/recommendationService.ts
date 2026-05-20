import { apiClient } from '../client';
import type { LibraryItem } from '../Media/libraryService';

export interface RecommendationListVM {
    title: string;
    description?: string;
    weight: number;
    items: LibraryItem[];
}

export const recommendationService = {
    getLibraryRecommendations: async (libraryId: string, providerId?: string, serverId?: string): Promise<RecommendationListVM[]> => {
        const response = await apiClient.get<RecommendationListVM[]>(`/libraries/${libraryId}/recommendations`, {
            params: { providerId },
            serverId
        });
        return response.data;
    },

    getProviders: async (serverId?: string): Promise<string[]> => {
        const response = await apiClient.get<string[]>('/recommendations/providers', { serverId });
        return response.data;
    },

    getGlobalRecommendations: async (providerId?: string, serverId?: string): Promise<RecommendationListVM[]> => {
        const response = await apiClient.get<RecommendationListVM[]>('/recommendations/global', {
            params: { providerId },
            serverId
        });
        return response.data;
    }

};