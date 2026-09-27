import { apiClient } from '../client';

export interface FileSystemRoot {
    label: string;
    path: string;
}

export interface FileSystemEntry {
    name: string;
    path: string;
    hasChildren: boolean;
}

export interface FileSystemListing {
    path: string;
    parentPath: string | null;
    folders: FileSystemEntry[];
}

export const filesystemService = {
    getRoots: async (serverId?: string): Promise<FileSystemRoot[]> => {
        const response = await apiClient.get<FileSystemRoot[]>('/admin/filesystem/roots', { serverId });
        return response.data;
    },

    list: async (path: string, serverId?: string): Promise<FileSystemListing> => {
        const response = await apiClient.get<FileSystemListing>('/admin/filesystem/list', {
            serverId,
            params: { path }
        });
        return response.data;
    }
};
