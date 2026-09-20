import { apiClient } from '../client';
import type { PlaylistMediaType } from '../Music/smartPlaylistService';

export interface PlaylistSummaryVM {
    id: string;
    name: string;
    description?: string;
    mediaType: PlaylistMediaType;
    itemCount: number;
    posterUrls: string[];
    backdropUrls: string[];
}

export interface PlaylistItemVM {
    id: string;
    mediaItemId: string;
    order: number;
    title: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    releaseYear?: number;
    type: string;
    posterUrl?: string;
    backgroundUrl?: string;
    durationMinutes?: number;
    contentRating?: string;
    isPlayed: boolean;
    resumePositionSeconds: number;

    artistName?: string;
    albumTitle?: string;
    albumId?: string;
    albumArtworkUrl?: string;
    trackNumber?: number;
    durationSeconds?: number;
}

export interface PlaylistDetailsVM extends PlaylistSummaryVM {
    items: PlaylistItemVM[];
}

export const playlistService = {
    getPlaylists: async (serverId?: string): Promise<PlaylistSummaryVM[]> => {
        const response = await apiClient.get<PlaylistSummaryVM[]>('/playlists', { serverId });
        return response.data;
    },
    getPlaylist: async (id: string, serverId?: string): Promise<PlaylistDetailsVM> => {
        const response = await apiClient.get<PlaylistDetailsVM>(`/playlists/${id}`, { serverId });
        return response.data;
    },
    createPlaylist: async (name: string, description?: string, mediaType: PlaylistMediaType = 'Mixed', serverId?: string): Promise<{ id: string }> => {
        const response = await apiClient.post<{ id: string }>('/playlists', { name, description, mediaType }, { serverId });
        return response.data;
    },
    addToPlaylist: async (playlistId: string, mediaId: string, serverId?: string) => {
        await apiClient.post(`/playlists/${playlistId}/items/${mediaId}`, null, { serverId });
    },
    removeFromPlaylist: async (playlistId: string, itemId: string, serverId?: string) => {
        await apiClient.delete(`/playlists/${playlistId}/items/${itemId}`, { serverId });
    },
    reorderPlaylist: async (playlistId: string, itemIds: string[], serverId?: string) => {
        await apiClient.put(`/playlists/${playlistId}/reorder`, { playlistItemIds: itemIds }, { serverId });
    },
    markAllUnplayed: async (playlistId: string, serverId?: string) => {
        await apiClient.post(`/playlists/${playlistId}/unwatch-all`, null, { serverId });
    },
    deletePlaylist: async (playlistId: string, serverId?: string) => {
        await apiClient.delete(`/playlists/${playlistId}`, { serverId });
    },
    getPlaylistsContainingItem: async (mediaId: string, serverId?: string): Promise<string[]> => {
        const response = await apiClient.get<string[]>(`/playlists/contains/${mediaId}`, { serverId });
        return response.data;
    },
    removeMediaFromPlaylist: async (playlistId: string, mediaId: string, serverId?: string) => {
        await apiClient.delete(`/playlists/${playlistId}/media/${mediaId}`, { serverId });
    },
    updatePlaylist: async (playlistId: string, name: string, description?: string, serverId?: string) => {
        await apiClient.put(`/playlists/${playlistId}`, { name, description }, { serverId });
    }
};