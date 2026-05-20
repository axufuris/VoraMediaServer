import { apiClient } from '../client';
import type { LibraryItem } from '../Media/libraryService';

export interface SmartListRulesDto {
    genreIds?: number[];
    decade?: number;
    unwatchedOnly?: boolean;
    mediaTypes?: string[];
    contentRating?: string;
}

export interface SmartListClientDto {
    id: string;
    title: string;
    displayOrder: number;
    isSpotlight: boolean;
}

export interface SmartListAdminDto {
    id: string;
    title: string;
    filterRulesJson: string;
    sortBy: number;
    maxItems: number;
    displayOrder: number;
    showOnHomepage: boolean;
    showToFriends: boolean;
    collectionId?: string;
    isSystemList: boolean;
    isSpotlight: boolean;
}

export type CreateSmartListRequest = Omit<SmartListAdminDto, 'id'>;

export const smartListService = {
    getActiveLists: async (serverId?: string): Promise<SmartListClientDto[]> => {
        const response = await apiClient.get<SmartListClientDto[]>('/smartlists/active', { serverId });
        return response.data;
    },
    getListItems: async (listId: string, serverId?: string): Promise<LibraryItem[]> => {
        const response = await apiClient.get<LibraryItem[]>(`/smartlists/${listId}/items`, { serverId });
        return response.data;
    },

    getAllLists: async (serverId?: string): Promise<SmartListAdminDto[]> => {
        const response = await apiClient.get<SmartListAdminDto[]>('/admin/smartlists', { serverId });
        return response.data;
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