import { apiClient, getResponseStatus } from '../client';
import type { ArtistTrackVM } from './musicService';

export type PlaylistMediaType = 'Mixed' | 'Music' | 'Movies' | 'Shows';

export type SmartPlaylistField =
    | 'Title' | 'Artist' | 'AlbumTitle' | 'AlbumArtist' | 'Genre' | 'Year'
    | 'DurationSeconds' | 'ContentRating' | 'PlayCount' | 'LastPlayedAt'
    | 'DateAdded' | 'Liked' | 'TrackNumber' | 'DiscNumber' | 'LibraryId' | 'IsCompilation'
    | 'ReleaseYear' | 'ShowTitle' | 'SeasonNumber' | 'EpisodeNumber' | 'IsWatched' | 'Rating';

export type SmartPlaylistOperator =
    | 'Equals' | 'NotEquals' | 'Contains' | 'NotContains' | 'StartsWith' | 'EndsWith'
    | 'GreaterThan' | 'LessThan' | 'Between'
    | 'InLastDays' | 'NotInLastDays'
    | 'IsNull' | 'IsNotNull';

export type SmartPlaylistSortBy =
    | 'Random' | 'Title' | 'ArtistName' | 'AlbumTitle' | 'Year'
    | 'DateAdded' | 'LastPlayedAt' | 'PlayCount' | 'DurationSeconds';

export type SmartPlaylistSortDirection = 'Asc' | 'Desc';

export type SmartPlaylistMatch = 'All' | 'Any';

export interface SmartPlaylistRule {
    field: SmartPlaylistField;
    operator: SmartPlaylistOperator;
    value?: string;
    secondValue?: string;
}

export interface SmartPlaylistRuleGroup {
    match: SmartPlaylistMatch;
    rules: SmartPlaylistRule[];
    groups: SmartPlaylistRuleGroup[];
}

export interface SmartPlaylistDefinition {
    root: SmartPlaylistRuleGroup;
    limit?: number;
    sortBy: SmartPlaylistSortBy;
    sortDirection: SmartPlaylistSortDirection;
}

export interface SmartPlaylistSummaryVM {
    id: string;
    name: string;
    description?: string;
    artworkUrl?: string;
    mediaType: PlaylistMediaType;
    trackCount: number;
    createdAt: string;
    updatedAt: string;
}

export interface SmartPlaylistDetailVM {
    id: string;
    name: string;
    description?: string;
    artworkUrl?: string;
    mediaType: PlaylistMediaType;
    definition: SmartPlaylistDefinition;
    createdAt: string;
    updatedAt: string;
}

export interface SmartPlaylistSaveRequest {
    name: string;
    description?: string | null;
    artworkUrl?: string | null;
    mediaType: PlaylistMediaType;
    definition: SmartPlaylistDefinition;
}

export interface SmartPlaylistMovieVM {
    id: string;
    title: string;
    year?: number;
    posterUrl?: string;
    backgroundUrl?: string;
    durationSeconds?: number;
    contentRating?: string;
    isWatched: boolean;
}

export interface SmartPlaylistEpisodeVM {
    id: string;
    title: string;
    showTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    posterUrl?: string;
    durationSeconds?: number;
    contentRating?: string;
    isWatched: boolean;
}

export interface SmartPlaylistItemsVM {
    mediaType: PlaylistMediaType;
    tracks?: ArtistTrackVM[];
    movies?: SmartPlaylistMovieVM[];
    episodes?: SmartPlaylistEpisodeVM[];
}

export const emptyRuleGroup = (): SmartPlaylistRuleGroup => ({
    match: 'All',
    rules: [],
    groups: []
});

export const emptyDefinition = (): SmartPlaylistDefinition => ({
    root: emptyRuleGroup(),
    limit: 100,
    sortBy: 'Random',
    sortDirection: 'Asc'
});

export const PLAYLIST_MEDIA_TYPES: { value: PlaylistMediaType; label: string }[] = [
    { value: 'Music', label: 'Music' },
    { value: 'Movies', label: 'Movies' },
    { value: 'Shows', label: 'Shows' },
    { value: 'Mixed', label: 'Mixed' }
];

export const smartPlaylistService = {
    list: async (serverId?: string): Promise<SmartPlaylistSummaryVM[]> => {
        const response = await apiClient.get<SmartPlaylistSummaryVM[]>('/smart-playlists/', { serverId });
        return response.data;
    },

    get: async (id: string, serverId?: string): Promise<SmartPlaylistDetailVM | null> => {
        try {
            const response = await apiClient.get<SmartPlaylistDetailVM>(`/smart-playlists/${id}`, { serverId });
            return response.data;
        } catch (err: unknown) {
            const status = getResponseStatus(err);
            if (status === 404) return null;
            throw err;
        }
    },

    create: async (request: SmartPlaylistSaveRequest, serverId?: string): Promise<SmartPlaylistSummaryVM> => {
        const response = await apiClient.post<SmartPlaylistSummaryVM>('/smart-playlists/', request, { serverId });
        return response.data;
    },

    update: async (id: string, request: SmartPlaylistSaveRequest, serverId?: string): Promise<SmartPlaylistSummaryVM> => {
        const response = await apiClient.put<SmartPlaylistSummaryVM>(`/smart-playlists/${id}`, request, { serverId });
        return response.data;
    },

    remove: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/smart-playlists/${id}`, { serverId });
    },

    getItems: async (id: string, serverId?: string): Promise<SmartPlaylistItemsVM> => {
        const response = await apiClient.get<SmartPlaylistItemsVM>(`/smart-playlists/${id}/items`, { serverId });
        return response.data;
    },

    preview: async (mediaType: PlaylistMediaType, definition: SmartPlaylistDefinition, serverId?: string): Promise<number> => {
        const response = await apiClient.post<{ count: number }>('/smart-playlists/preview', { mediaType, definition }, { serverId });
        return response.data.count;
    }
};
