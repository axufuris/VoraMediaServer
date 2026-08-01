import { apiClient } from '../client';
import type {
    LibraryMigrationJobVM,
    LibrarySyncPinStatusVM,
    LibrarySyncPinVM,
    LibrarySyncProviderVM,
    RemoteLibraryVM,
    RemoteServerVM
} from './libraryMigrationService';

export interface RunSelfLibraryImportRequest {
    accessToken: string;
    serverClientIdentifier: string;
    serverName: string;
    connectionUri: string;
    includeWatchState: boolean;
    includeRatings: boolean;
    librarySectionKeys: string[];
    plexUsername?: string | null;
}

const BASE = '/library-import/providers';

export const libraryImportService = {
    getProviders: async (serverId?: string): Promise<LibrarySyncProviderVM[]> => {
        const response = await apiClient.get<LibrarySyncProviderVM[]>('/library-import/providers', { serverId });
        return response.data;
    },
    createPin: async (providerId: string, serverId?: string): Promise<LibrarySyncPinVM> => {
        const response = await apiClient.post<LibrarySyncPinVM>(BASE + '/' + providerId + '/pin', undefined, { serverId });
        return response.data;
    },
    pollPin: async (providerId: string, pinId: string, serverId?: string): Promise<LibrarySyncPinStatusVM> => {
        const response = await apiClient.get<LibrarySyncPinStatusVM>(BASE + '/' + providerId + '/pin/' + pinId, { serverId });
        return response.data;
    },
    listServers: async (providerId: string, accessToken: string, serverId?: string): Promise<RemoteServerVM[]> => {
        const response = await apiClient.post<RemoteServerVM[]>(BASE + '/' + providerId + '/servers', { accessToken }, { serverId });
        return response.data;
    },
    listLibraries: async (providerId: string, accessToken: string, connectionUri: string, serverId?: string): Promise<RemoteLibraryVM[]> => {
        const response = await apiClient.post<RemoteLibraryVM[]>(BASE + '/' + providerId + '/libraries', { accessToken, connectionUri }, { serverId });
        return response.data;
    },
    runImport: async (providerId: string, request: RunSelfLibraryImportRequest, serverId?: string): Promise<LibraryMigrationJobVM> => {
        const response = await apiClient.post<LibraryMigrationJobVM, RunSelfLibraryImportRequest>(BASE + '/' + providerId + '/run', request, { serverId });
        return response.data;
    },
    getJob: async (jobId: string, serverId?: string): Promise<LibraryMigrationJobVM> => {
        const response = await apiClient.get<LibraryMigrationJobVM>('/library-import/jobs/' + jobId, { serverId });
        return response.data;
    }
};
