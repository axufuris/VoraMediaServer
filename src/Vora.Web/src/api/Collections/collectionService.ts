import { apiClient } from '../client';

export type CollectionSortOrder =
    | 'ReleaseDateAsc'
    | 'ReleaseDateDesc'
    | 'DateAddedDesc'
    | 'Alphabetical'
    | 'Chronological';

export interface CollectionSummary {
    id: string;
    title: string;
    posterUrl?: string;
    itemCount: number;
    sortTitle?: string;
    visibleStartDate?: string;
    visibleEndDate?: string;
    systemGenerated: boolean;
}

export interface CollectionDetails {
    id: string;
    title: string;
    description?: string;
    posterUrl?: string;
    backdropUrl?: string;
    defaultSortName: string;
    isMixedCollection: boolean;
    itemCount: number;
    lockedFields: string[];
    items: CollectionDetailsLibraryItem[];

    defaultSort: CollectionSortOrder;
    libraryId?: string;
    sortProviderId?: string;
    externalListId?: string;
    autoSyncChronology: boolean;

    sortTitle?: string;
    visibleStartDate?: string;
    visibleEndDate?: string;
    systemGenerated: boolean;
    contentSyncProviderId?: string;
    contentSyncExternalId?: string;
    syncIntervalDays: number;
    mirrorList: boolean;
}

export interface CollectionDetailsLibraryItem {
    id: string;
    title: string;
    sortTitle?: string;
    releaseDate?: string;
    type: string;
    tvShowTitle?: string;
    posterUrl?: string;
    isPlayed?: boolean;
    unplayedItemCount?: number;
    inUniverseYear?: number | null;
    inUniverseYearLocked?: boolean;
}

export const collectionService = {
    getLibraryCollections: async (libraryId: string, serverId?: string): Promise<CollectionSummary[]> => {
        const response = await apiClient.get<CollectionSummary[]>(`/collections/library/${libraryId}`, { serverId });
        return response.data;
    },

    getCollectionDetails: async (collectionId: string, serverId?: string, sort?: CollectionSortOrder): Promise<CollectionDetails> => {
        const response = await apiClient.get<CollectionDetails>(`/collections/${collectionId}`, {
            serverId,
            params: sort ? { sort } : undefined
        });
        return response.data;
    },

    getAllCollections: async (serverId?: string): Promise<CollectionSummary[]> => {
        const response = await apiClient.get<CollectionSummary[]>('/collections', { serverId });
        return response.data;
    },

    getGlobalCollections: async (serverId?: string): Promise<CollectionSummary[]> => {
        const response = await apiClient.get<CollectionSummary[]>('/collections/global', { serverId });
        return response.data;
    }
};