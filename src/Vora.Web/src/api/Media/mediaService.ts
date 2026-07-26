import { apiClient } from '../client';

export interface MediaVideo {
    videoKey: string;
    name?: string;
    site?: string;
    type?: string;
    isOfficial: boolean;
}

export interface MediaExtra {
    id: string;
    title: string;
    extraType: string;
    container?: string | null;
}

export interface VideoTrack {
    id: string;
    codec?: string;
    profile?: string;
    hdrType?: string;
    bitDepth?: number;
    isDefault?: boolean;
}

export interface AudioTrack {
    id: string;
    codec?: string;
    language?: string;
    channels?: number;
    title?: string;
    isDefault?: boolean;
}

export interface SubtitleTrack {
    id: string;
    codec?: string;
    language?: string;
    title?: string;
    isForced?: boolean;
    isDefault?: boolean;
}

export interface MediaPart {
    id: string;
    resolution?: string;
    edition?: string;
    fileSizeBytes?: number;
    bitrateKbps?: number;
    filePath: string;
    videoTracks?: VideoTrack[];
    audioTracks?: AudioTrack[];
    subtitleTracks?: SubtitleTrack[];
}

export interface CastMember {
    actorId: string;
    tmdbId: number;
    name: string;
    characterName?: string;
    profileImageUrl?: string;
    order: number;
    role: string;
}

export interface Season {
    id: string;
    seasonNumber: number;
    title: string;
    posterUrl?: string;
    episodeCount?: number;
    isPlayed?: boolean;
    unplayedItemCount?: number;
    serverAdminRating?: number;
    myRating?: number;
}

export interface Episode {
    id: string;
    episodeNumber: number;
    title: string;
    overview?: string;
    posterUrl?: string;
    releaseDate?: string;
    durationMinutes?: number;
    isPlayed?: boolean;
    resumePositionSeconds?: number;
    serverAdminRating?: number;
    myRating?: number;
}

export interface SeasonDetails {
    id: string;
    seasonNumber: number;
    title?: string;
    overview?: string;
    posterUrl?: string;
    tvShowId: string;
    tvShowTitle: string;
    upcomingEpisodesJson?: string;
    episodes: Episode[];
    lockedFields?: string[];
}

export type MediaMarkerType = 'Intro' | 'Recap' | 'Preview' | 'Credits' | 'CreditsScene';

export interface MediaMarker {
    type: MediaMarkerType;
    startSeconds: number;
    endSeconds: number;
    order: number;
}

export interface MediaItem {
    id: string;
    title: string;
    sortTitle?: string;
    overview?: string;
    durationMinutes?: number;
    contentRating?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    releaseDate?: string;
    type: string;
    numberOfSeasons?: number;
    seasonNumber?: number;
    episodeNumber?: number;
    isPlayed?: boolean;
    resumePositionSeconds?: number;
    unplayedItemCount?: number;
    libraryId: string;
    libraryArtworkProviderId?: string;
    upcomingEpisodesJson?: string;
    collectionIds: string[];
    genres?: string[];
    seasons?: Season[];
    cast?: CastMember[];
    videos?: MediaVideo[];
    extras?: MediaExtra[];
    lockedFields?: string[];
    episodes?: Episode[];
    tvShowTitle?: string;
    tvShowId?: string;
    seasonId?: string;
    thirdPartyRating1?: number;
    thirdPartyRating1Name?: string;
    thirdPartyRating2?: number;
    thirdPartyRating2Name?: string;
    serverAdminRating?: number;
    myRating?: number;
    mediaParts?: MediaPart[];
    markers?: MediaMarker[];
}

export interface UpNextItemVM {
    id: string;
    title: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    type: string;
    posterUrl?: string;
    backgroundUrl?: string;
    overview?: string;
}

export interface RelatedListVM {
    title: string;
    items: UpNextItemVM[];
}

export interface UpNextResultVM {
    nextItem?: UpNextItemVM;
    previousItem?: UpNextItemVM;
    relatedLists: RelatedListVM[];
}

export const mediaService = {
    getMediaItem: async (mediaItemId: string, serverId?: string): Promise<MediaItem> => {
        const response = await apiClient.get<MediaItem>(`/media/${mediaItemId}`, { serverId });
        return response.data;
    },

    getSeason: async (seasonId: string, serverId?: string): Promise<SeasonDetails> => {
        const response = await apiClient.get<SeasonDetails>(`/seasons/${seasonId}`, { serverId });
        return response.data;
    },

    markAsPlayed: async (mediaItemId: string, isPlayed: boolean, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/played?isPlayed=${isPlayed}`, null, { serverId });
    },

    setRating: async (mediaItemId: string, rating: number | null, serverId?: string): Promise<void> => {
        await apiClient.put(`/media/${mediaItemId}/rating`, { rating }, { serverId });
    },

    getUpNext: async (mediaId: string, contextType?: string, contextId?: string, serverId?: string): Promise<UpNextResultVM> => {
        const response = await apiClient.get<UpNextResultVM>(`/media/${mediaId}/up-next`, {
            params: { contextType, contextId },
            serverId
        });
        return response.data;
    },

    getMarkers: async (mediaItemId: string, serverId?: string): Promise<MediaMarker[]> => {
        const response = await apiClient.get<MediaMarker[]>(`/media/${mediaItemId}/markers`, { serverId });
        return response.data;
    },

    replaceMarkers: async (mediaItemId: string, markers: MediaMarker[], serverId?: string): Promise<void> => {
        await apiClient.put(`/media/${mediaItemId}/markers`, markers, { serverId });
    },

    getMarkersLocked: async (mediaItemId: string, serverId?: string): Promise<boolean> => {
        const response = await apiClient.get<{ locked: boolean }>(`/media/${mediaItemId}/markers/lock`, { serverId });
        return response.data.locked;
    },

    setMarkersLocked: async (mediaItemId: string, locked: boolean, serverId?: string): Promise<void> => {
        await apiClient.put(`/media/${mediaItemId}/markers/lock`, { locked }, { serverId });
    }
};
