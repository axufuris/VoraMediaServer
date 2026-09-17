import { apiClient, createDirectClient } from '../client';
import type { UserProfileVM } from './profileService';

export interface UserVM {
    id: string;
    email: string;
    displayName: string;
    isAdmin: boolean;
    hasAllLibraryAccess: boolean;
    allowedLibraryIds: string[];
    canRequestMedia: boolean;
    autoApproveRequests: boolean;
    enableAiRecommendations: boolean;
    emailNotifyOnRequestAvailable: boolean;
    hasAllIptvAccess: boolean;
    allowedIptvPlaylistIds: string[];
    canRecordLiveTv: boolean;
    dvrStorageQuotaBytes: number;
    canTimeshiftIptv: boolean;
    canAddCustomPodcastFeeds: boolean;
    profiles: UserProfileVM[];
}

export interface UserProfileHistoryDto {
    sessionId: string;
    title: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    releaseYear?: number;
    type: string;
    contentRating?: string;
    durationMinutes: number;
    pausedMinutes: number;
    timeStarted: string;
    timeStopped?: string;
    profileId: string;
    profileName: string;
    isGrouped: boolean;
    subSessions?: UserProfileHistoryDto[];
}

export const userService = {
    getAllUsers: async (serverId?: string): Promise<UserVM[]> => {
        const response = await apiClient.get<UserVM[]>('/users', { serverId });
        return response.data;
    },

    getUserAccount: async (userId: string, serverId?: string): Promise<UserVM> => {
        const response = await apiClient.get<UserVM>(`/users/${userId}`, { serverId });
        return response.data;
    },

    updateAccount: async (userId: string, email: string, displayName: string, newPassword?: string, emailNotifyOnRequestAvailable?: boolean, serverId?: string): Promise<{ emailVerificationSent: boolean }> => {
        const response = await apiClient.put<{ emailVerificationSent: boolean }>(`/users/${userId}`, { email, displayName, newPassword, emailNotifyOnRequestAvailable }, { serverId });
        return response.data ?? { emailVerificationSent: false };
    },

    updateUserAccess: async (userId: string, hasAllLibraryAccess: boolean, allowedLibraryIds: string[], canRequestMedia: boolean, autoApproveRequests: boolean, enableAiRecommendations: boolean, hasAllIptvAccess: boolean, allowedIptvPlaylistIds: string[], canRecordLiveTv: boolean, dvrStorageQuotaBytes: number, canTimeshiftIptv: boolean, canAddCustomPodcastFeeds: boolean, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/${userId}/access`, {
            hasAllLibraryAccess, allowedLibraryIds, canRequestMedia, autoApproveRequests,
            enableAiRecommendations, hasAllIptvAccess, allowedIptvPlaylistIds,
            canRecordLiveTv, dvrStorageQuotaBytes, canTimeshiftIptv, canAddCustomPodcastFeeds
        }, { serverId });
    },

    getPlayHistory: async (userId: string, profileId: string | null, page: number, pageSize: number, search: string, typeFilter: string, serverId?: string): Promise<{ data: UserProfileHistoryDto[], total: number }> => {
        const response = await apiClient.get<{ data: UserProfileHistoryDto[], total: number }>(`/users/${userId}/play-history`, {
            params: { profileId, page, pageSize, search, typeFilter },
            serverId
        });
        return response.data;
    },

    getUserAccountWithToken: async (baseUrl: string, token: string, userId: string): Promise<UserVM> => {
        const client = createDirectClient(baseUrl, token);
        const response = await client.get<UserVM>(`/users/${userId}`);
        return response.data;
    }
};
