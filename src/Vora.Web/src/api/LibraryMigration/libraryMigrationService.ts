import { apiClient } from '../client';

export interface LibrarySyncProviderVM {
    id: string;
    name: string;
    providerName: string;
    description: string;
}

export interface LibrarySyncPinVM {
    pinId: string;
    code: string;
    verificationUrl: string;
    expiresAt?: string | null;
}

export type LibrarySyncPinStatus = 'Pending' | 'Authorized' | 'Expired';

export interface LibrarySyncTokenVM {
    accessToken: string;
    username?: string | null;
}

export interface LibrarySyncPinStatusVM {
    pinId: string;
    status: LibrarySyncPinStatus;
    token?: LibrarySyncTokenVM | null;
}

export interface RemoteConnectionVM {
    uri: string;
    isLocal: boolean;
    isHttps: boolean;
    isRelay: boolean;
}

export interface RemoteServerVM {
    clientIdentifier: string;
    name: string;
    isOwned: boolean;
    ownerName?: string | null;
    platform?: string | null;
    productVersion?: string | null;
    isOnline: boolean;
    connections: RemoteConnectionVM[];
}

export type RemoteAccountKind = 'Owner' | 'Home' | 'Shared';

export interface RemoteAccountVM {
    id: string;
    displayName: string;
    kind: RemoteAccountKind;
    hasPin: boolean;
    avatarUrl?: string | null;
    email?: string | null;
}

export type RemoteLibraryKind = 'Movie' | 'Show' | 'Music' | 'Other';

export interface RemoteLibraryVM {
    key: string;
    name: string;
    kind: RemoteLibraryKind;
}

export type LibraryMigrationJobState = 'Pending' | 'Running' | 'Completed' | 'Failed';
export type LibraryMigrationUserState = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped';

export interface LibraryMigrationUserStatusVM {
    accountId: string;
    accountName: string;
    profileId: string;
    profileName: string;
    state: LibraryMigrationUserState;
    watchStatesFetched: number;
    watchStatesImported: number;
    ratingsFetched: number;
    ratingsImported: number;
    skipped: number;
    errorMessage?: string | null;
}

export interface LibraryMigrationJobVM {
    jobId: string;
    providerId: string;
    serverName: string;
    state: LibraryMigrationJobState;
    startedAt: string;
    completedAt?: string | null;
    errorMessage?: string | null;
    users: LibraryMigrationUserStatusVM[];
}

export interface RunLibraryMigrationMappingInput {
    accountId: string;
    accountName: string;
    profileId: string;
    profileName: string;
    pin?: string | null;
}

export interface RunLibraryMigrationRequest {
    accessToken: string;
    serverClientIdentifier: string;
    serverName: string;
    connectionUri: string;
    includeWatchState: boolean;
    includeRatings: boolean;
    librarySectionKeys: string[];
    mappings: RunLibraryMigrationMappingInput[];
}

const BASE = '/library-migration/providers';

export const libraryMigrationService = {
    getProviders: async (serverId?: string): Promise<LibrarySyncProviderVM[]> => {
        const response = await apiClient.get<LibrarySyncProviderVM[]>('/library-migration/providers', { serverId });
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
    listAccounts: async (providerId: string, accessToken: string, serverId?: string): Promise<RemoteAccountVM[]> => {
        const response = await apiClient.post<RemoteAccountVM[]>(BASE + '/' + providerId + '/accounts', { accessToken }, { serverId });
        return response.data;
    },
    listLibraries: async (providerId: string, accessToken: string, connectionUri: string, serverId?: string): Promise<RemoteLibraryVM[]> => {
        const response = await apiClient.post<RemoteLibraryVM[]>(BASE + '/' + providerId + '/libraries', { accessToken, connectionUri }, { serverId });
        return response.data;
    },
    runMigration: async (providerId: string, request: RunLibraryMigrationRequest, serverId?: string): Promise<LibraryMigrationJobVM> => {
        const response = await apiClient.post<LibraryMigrationJobVM, RunLibraryMigrationRequest>(BASE + '/' + providerId + '/run', request, { serverId });
        return response.data;
    },
    getJob: async (jobId: string, serverId?: string): Promise<LibraryMigrationJobVM> => {
        const response = await apiClient.get<LibraryMigrationJobVM>('/library-migration/jobs/' + jobId, { serverId });
        return response.data;
    }
};
