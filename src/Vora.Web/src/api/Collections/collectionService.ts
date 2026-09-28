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
    // Only sent to admins (includeHidden): why viewers don't see it.
    hiddenReason?: CollectionHiddenReason | null;
}

export type CollectionHiddenReason = 'Empty' | 'AutomaticCollectionsHidden' | 'BelowMinimumSize';

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
    // Set on automatic collections; lets an admin ask TMDB for the description again.
    tmdbId?: number | null;
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
    rulesJson?: string;
    smartMediaType?: 'Mixed' | 'Music' | 'Movies' | 'Shows';
}

export interface CollectionDetailsLibraryItem {
    id: string;
    title: string;
    sortTitle?: string;
    releaseDate?: string;
    type: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    seasonName?: string;
    episodeNumber?: number;
    edition?: string;
    posterUrl?: string;
    isPlayed?: boolean;
    unplayedItemCount?: number;
    inUniverseYear?: number | null;
    inUniverseYearLocked?: boolean;
}

export const collectionService = {
    // includeHidden is honoured for admins only; everyone else gets what viewers see.
    getLibraryCollections: async (libraryId: string, serverId?: string, includeHidden = false): Promise<CollectionSummary[]> => {
        const response = await apiClient.get<CollectionSummary[]>(`/collections/library/${libraryId}`, {
            serverId,
            params: includeHidden ? { includeHidden: true } : undefined,
        });
        return response.data;
    },

    refreshDescription: async (collectionId: string, serverId?: string): Promise<string> => {
        const response = await apiClient.post<{ description: string }>(`/collections/${collectionId}/description/refresh`, null, { serverId });
        return response.data.description;
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