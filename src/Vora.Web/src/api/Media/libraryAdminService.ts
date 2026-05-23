import { apiClient } from '../client';
import type { MediaLibrary } from './libraryService';

export interface UpdateMediaRequest {
    title: string;
    sortTitle?: string;
    overview?: string;
    contentRating?: string;
    releaseDate?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    thirdPartyRating1ProviderId?: string;
    thirdPartyRating2ProviderId?: string;
    artworkProviderId?: string;
    lockedFields: string[];
}

export interface UpdateSeasonRequest {
    title?: string;
    overview?: string;
    posterUrl?: string;
    lockedFields: string[];
}

export interface CreateLibraryRequest {
    name: string;
    type: number;
    folderPaths: string[];
    enableRealTimeWatching: boolean;
    useLocalAssets: boolean;
    findExtras: boolean;
    onlyShowTrailers: boolean;
    enableVideoPreviewThumbnails: boolean;
    collectionDisplay: number;
    enableCreditsDetection: boolean;
    enableVoiceActivityDetection: boolean;
    minimumCollectionSize: number;
    metadataProviderId: string;
    thirdPartyRating1ProviderId?: string;
    thirdPartyRating2ProviderId?: string;
    artworkProviderId?: string;
    episodeSorting: number;
    episodeOrder: number;
    useSeasonTitles: boolean;
    seasonsDisplay: number;
    enableIntroDetection: boolean;
}

export type UpdateLibraryRequest = Omit<MediaLibrary, 'id' | 'type' | 'isBeingWatched'>;

export interface MarkerCoverageVM {
    libraryId: string;
    libraryName: string;
    totalItems: number;
    itemsWithAnyMarker: number;
    itemsWithIntro: number;
    itemsWithCredits: number;
    itemsWithCreditsScene: number;
    itemsWithRecap: number;
    itemsWithPreview: number;
    itemsMissingDuration: number;
}

export const libraryAdminService = {
    createLibrary: async (request: CreateLibraryRequest, serverId?: string): Promise<string> => {
        const response = await apiClient.post<string>('/libraries', request, { serverId });
        return response.data;
    },

    updateLibrary: async (id: string, request: UpdateLibraryRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/libraries/${id}`, request, { serverId });
    },

    deleteLibrary: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/libraries/${id}`, { serverId });
    },

    triggerScan: async (libraryId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/libraries/${libraryId}/scan`, null, { serverId });
    },

    refreshMetadata: async (libraryId: string, force: boolean = false, serverId?: string): Promise<void> => {
        await apiClient.post(`/libraries/${libraryId}/metadata?force=${force}`, null, { serverId });
    },

    refreshRatings: async (libraryId: string, force: boolean = false, serverId?: string): Promise<void> => {
        await apiClient.post(`/libraries/${libraryId}/ratings?force=${force}`, null, { serverId });
    },

    refreshActorMetadata: async (serverId?: string): Promise<void> => {
        await apiClient.post('/metadata/actors/refresh', null, { serverId });
    },

    toggleWatch: async (libraryId: string, enable: boolean, serverId?: string): Promise<void> => {
        await apiClient.post(`/libraries/${libraryId}/watchfolder?enable=${enable}`, null, { serverId });
    },

    updateMediaItem: async (id: string, request: UpdateMediaRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/media/${id}`, request, { serverId });
    },

    deleteMediaItem: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/media/${id}`, { serverId });
    },

    updateSeason: async (id: string, request: UpdateSeasonRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/seasons/${id}`, request, { serverId });
    },

    refreshItemMetadata: async (mediaItemId: string, force: boolean = false, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/metadata?force=${force}`, null, { serverId });
    },

    analyzeMedia: async (mediaItemId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/analyze`, null, { serverId });
    },

    triggerMediaScan: async (mediaItemId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/scan`, null, { serverId });
    },

    analyzeLibrary: async (libraryId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/libraries/${libraryId}/analyze`, null, { serverId });
    },

    getLibraryMarkerCoverage: async (libraryId: string, serverId?: string): Promise<MarkerCoverageVM> => {
        const response = await apiClient.get<MarkerCoverageVM>(`/libraries/${libraryId}/marker-coverage`, { serverId });
        return response.data;
    }
};