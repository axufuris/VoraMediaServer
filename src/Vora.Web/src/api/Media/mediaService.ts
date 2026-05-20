import { apiClient } from '../client';

export interface MediaVideo {
    videoKey: string;
    name?: string;
    site?: string;
    type?: string;
    isOfficial: boolean;
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
    upcomingEpisodesJson?: string;
    collectionIds: string[];
    genres?: string[];
    seasons?: Season[];
    cast?: CastMember[];
    videos?: MediaVideo[];
    lockedFields?: string[];
    episodes?: Episode[];
    tvShowTitle?: string;
    tvShowId?: string;
    thirdPartyRating1?: number;
    thirdPartyRating1Name?: string;
    thirdPartyRating2?: number;
    thirdPartyRating2Name?: string;
    mediaParts?: MediaPart[];
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

    getUpNext: async (mediaId: string, contextType?: string, contextId?: string, serverId?: string): Promise<UpNextResultVM> => {
        const response = await apiClient.get<UpNextResultVM>(`/media/${mediaId}/up-next`, {
            params: { contextType, contextId },
            serverId
        });
        return response.data;
    }
};
