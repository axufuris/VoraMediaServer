import { apiClient } from '../client';

export interface MediaLibrary {
    id: string;
    name: string;
    type: string;
    folderPaths: string[];
    scanner: string;
    useLocalAssets: boolean;
    findExtras: boolean;
    onlyShowTrailers: boolean;
    enableVideoPreviewThumbnails: boolean;
    collectionDisplay: number;
    enableCreditsDetection: boolean;
    enableVoiceActivityDetection: boolean;
    episodeSorting: number;
    episodeOrder: number;
    useSeasonTitles: boolean;
    seasonsDisplay: number;
    enableIntroDetection: boolean;
    minimumCollectionSize: number;
    isBeingWatched: boolean;
    enableRealTimeWatching: boolean;
    metadataProviderId: string;
    thirdPartyRating1ProviderId?: string;
    thirdPartyRating2ProviderId?: string;
    artworkProviderId?: string;
}

export interface LibrarySummary {
    id: string;
    name: string;
    type: string;
    isBeingWatched: boolean;
}

export interface LibraryItem {
    id: string;
    title: string;
    sortTitle?: string;
    releaseDate?: string;
    type: string;
    posterUrl?: string;
    backgroundUrl?: string;
    contentRating?: string;
    resolution?: string;
    numberOfSeasons?: number;
    libraryId: string;
    timelineOrder?: number;
    isPlayed?: boolean;
    unplayedItemCount?: number;
    serverAdminRating?: number;
    myRating?: number;
}

export const libraryService = {
    getLibraries: async (serverId?: string): Promise<LibrarySummary[]> => {
        const response = await apiClient.get<LibrarySummary[]>('/libraries', { serverId });
        return response.data;
    },

    getLibraryById: async (id: string, serverId?: string): Promise<MediaLibrary> => {
        const response = await apiClient.get<MediaLibrary>(`/libraries/${id}`, { serverId });
        return response.data;
    },

    getLibraryMedia: async (libraryId: string, serverId?: string): Promise<LibraryItem[]> => {
        const response = await apiClient.get<LibraryItem[]>(`/libraries/${libraryId}/media`, { serverId });
        return response.data;
    }
};
