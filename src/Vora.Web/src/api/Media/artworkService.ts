import { apiClient } from '../client';

export interface ArtworkResult {
    id: string;
    url: string;
    isUserUploaded: boolean;
    type: string;
    language?: string;
    width?: number;
    height?: number;
    voteAverage?: number;
}

export const artworkService = {
    getArtworkOptions: async (mediaItemId: string, serverId?: string): Promise<ArtworkResult[]> => {
        const response = await apiClient.get<ArtworkResult[]>(`/media/${mediaItemId}/artwork`, { serverId });
        return response.data;
    },
    fetchProviderArtwork: async (mediaItemId: string, providerId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/artwork/fetch?providerId=${encodeURIComponent(providerId)}`, null, { serverId });
    }
};
