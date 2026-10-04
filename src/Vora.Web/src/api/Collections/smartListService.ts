import { apiClient } from '../client';
import type { LibraryItem } from '../Media/libraryService';
import type { IptvChannelVM } from '../Iptv/iptvAdminService';
import type { IptvRecordingSessionVM } from '../Iptv/dvrService';
import type { AlbumVM } from '../Music/musicService';
import type { PodcastFeedEpisodeVM } from '../Podcasts/podcastService';

export type SmartListSource =
    | 'Library'
    | 'FavoriteChannels'
    | 'FavoriteStations'
    | 'NewPodcastEpisodes'
    | 'RecentlyAddedMusic'
    | 'RecentRecordings';

export type SmartListSortBy =
    | 'DateAddedDesc'
    | 'ReleaseDateDesc'
    | 'ReleaseDateAsc'
    | 'Random'
    | 'TopRated'
    | 'MostWatched'
    | 'TitleAsc';

export interface SmartListRulesDto {
    genreIds?: number[];
    decade?: number;
    unwatchedOnly?: boolean;
    mediaTypes?: string[];
    contentRating?: string;
    days?: number;
}

export interface SmartListClientDto {
    id: string;
    title: string;
    displayOrder: number;
    source?: SmartListSource;
}

export type SmartListEntryKind = 'Media' | 'Channel' | 'Station' | 'PodcastEpisode' | 'Album' | 'Recording';

export interface SmartListEntry {
    kind: SmartListEntryKind;
    id: string;
    title: string;
    subtitle?: string | null;
    imageUrl?: string | null;
    media?: LibraryItem | null;
    channel?: IptvChannelVM | null;
    podcastEpisode?: PodcastFeedEpisodeVM | null;
    album?: AlbumVM | null;
    recording?: IptvRecordingSessionVM | null;
}

export interface SmartListAdminDto {
    id: string;
    title: string;
    source: SmartListSource;
    defaultKey?: string | null;
    libraryId?: string | null;
    filterRulesJson: string;
    sortBy: SmartListSortBy;
    maxItems: number;
    displayOrder: number;
    showOnHomepage: boolean;
    showToFriends: boolean;
    activeStartMonth?: number | null;
    activeStartDay?: number | null;
    activeEndMonth?: number | null;
    activeEndDay?: number | null;
    collectionId?: string | null;
}

export interface SmartListDefaultDto {
    key: string;
    title: string;
    source: SmartListSource;
    isPresent: boolean;
}

export type CreateSmartListRequest = Omit<SmartListAdminDto, 'id' | 'defaultKey'>;

export const smartListService = {
    getActiveLists: async (serverId?: string): Promise<SmartListClientDto[]> => {
        const response = await apiClient.get<SmartListClientDto[]>('/smartlists/active', { serverId });
        return response.data;
    },
    getListItems: async (listId: string, serverId?: string): Promise<LibraryItem[]> => {
        const response = await apiClient.get<LibraryItem[]>(`/smartlists/${listId}/items`, { serverId });
        return response.data;
    },
    getListEntries: async (listId: string, serverId?: string): Promise<SmartListEntry[]> => {
        const response = await apiClient.get<SmartListEntry[]>(`/smartlists/${listId}/entries`, { serverId });
        return response.data;
    },

    getAllLists: async (serverId?: string): Promise<SmartListAdminDto[]> => {
        const response = await apiClient.get<SmartListAdminDto[]>('/admin/smartlists', { serverId });
        return response.data;
    },
    getDefaults: async (serverId?: string): Promise<SmartListDefaultDto[]> => {
        const response = await apiClient.get<SmartListDefaultDto[]>('/admin/smartlists/defaults', { serverId });
        return response.data;
    },
    restoreDefaults: async (serverId?: string): Promise<number> => {
        const response = await apiClient.post<{ restored: number }>('/admin/smartlists/defaults/restore', undefined, { serverId });
        return response.data.restored;
    },
    createList: async (request: CreateSmartListRequest, serverId?: string): Promise<string> => {
        const response = await apiClient.post<string>('/admin/smartlists', request, { serverId });
        return response.data;
    },
    updateList: async (id: string, request: CreateSmartListRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/admin/smartlists/${id}`, request, { serverId });
    },
    reorderLists: async (listIds: string[], serverId?: string): Promise<void> => {
        await apiClient.put('/admin/smartlists/reorder', { listIds }, { serverId });
    },
    deleteList: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/smartlists/${id}`, { serverId });
    }
};
