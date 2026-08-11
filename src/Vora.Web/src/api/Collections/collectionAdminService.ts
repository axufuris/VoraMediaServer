import { apiClient } from '../client';
import type { ArtworkResult } from '../Media/artworkService';
import type { CollectionSortOrder } from './collectionService';

export interface UpdateCollectionRequest {
    title: string;
    description?: string;
    posterUrl?: string;
    backdropUrl?: string;
    defaultSort: CollectionSortOrder;
    lockedFields: string[];
    makeGlobal?: boolean;
    sortProviderId?: string;
    externalListId?: string;
    autoSyncChronology: boolean;
    sortTitle?: string;
    visibleStartDate?: string;
    visibleEndDate?: string;
    contentSyncProviderId?: string;
    contentSyncExternalId?: string;
    syncIntervalDays?: number;
    mirrorList?: boolean;
}

export interface CreateCollectionRequest {
    title: string;
    description?: string;
    posterUrl?: string;
    backdropUrl?: string;
    defaultSort: CollectionSortOrder;
    sortProviderId?: string;
    externalListId?: string;
    autoSyncChronology: boolean;
    sortTitle?: string;
    visibleStartDate?: string;
    visibleEndDate?: string;
    systemGenerated?: boolean;
    libraryId?: string;
    contentSyncProviderId?: string;
    contentSyncExternalId?: string;
    syncIntervalDays?: number;
    mirrorList?: boolean;
}

export const collectionAdminService = {
    createCollection: async (request: CreateCollectionRequest, serverId?: string): Promise<string> => {
        const response = await apiClient.post<{ id: string }>('/collections', request, { serverId });
        return response.data.id;
    },

    updateCollection: async (id: string, request: UpdateCollectionRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/collections/${id}`, request, { serverId });
    },

    addToCollection: async (collectionId: string, mediaId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/collections/${collectionId}/items/${mediaId}`, null, { serverId });
    },

    removeFromCollection: async (collectionId: string, mediaId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/collections/${collectionId}/items/${mediaId}`, { serverId });
    },

    syncChronology: async (collectionId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/collections/${collectionId}/sync-chronology`, null, { serverId });
    },

    reorderItems: async (collectionId: string, mediaItemIds: string[], serverId?: string): Promise<void> => {
        await apiClient.put(`/collections/${collectionId}/items/reorder`, { mediaItemIds }, { serverId });
    },

    setItemChronology: async (collectionId: string, mediaItemId: string, inUniverseYear: number | null, locked: boolean, serverId?: string): Promise<void> => {
        await apiClient.put(`/collections/${collectionId}/items/${mediaItemId}/chronology`, { inUniverseYear, locked }, { serverId });
    },

    deleteCollection: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/collections/${id}`, { serverId });
    },

    getArtworkOptions: async (collectionId: string, serverId?: string): Promise<ArtworkResult[]> => {
        const response = await apiClient.get<ArtworkResult[]>(`/collections/${collectionId}/artwork`, { serverId });
        return response.data;
    },

    fetchProviderArtwork: async (collectionId: string, providerId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/collections/${collectionId}/artwork/fetch?providerId=${providerId}`, null, { serverId });
    },

    deleteArtwork: async (artworkId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/collections/artwork/${artworkId}`, { serverId });
    }
};