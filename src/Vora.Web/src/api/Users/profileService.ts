import { apiClient, createDirectClient } from '../client';

export interface ProfileScheduleVM {
    dayOfWeek: number;
    startTime: string;
    endTime: string;
}

export interface UserProfileVM {
    id: string;
    name: string;
    isAdmin: boolean;
    profileImageUrl?: string;
    hasPin: boolean;
    allowedMovieRatings: string[];
    allowedTvRatings: string[];
    allowedMusicRatings: string[];
    blockUnratedContent: boolean;
    hasAllLibraryAccess: boolean;
    allowedLibraryIds: string[];
    hasAllIptvAccess: boolean;
    allowedIptvPlaylistIds: string[];
    accessSchedules: ProfileScheduleVM[];
    canRecordLiveTv: boolean;
    canAddCustomPodcastFeeds: boolean;
    lastFmUsername?: string;
}

export const profileService = {
    createProfile: async (userId: string, name: string, profileImageUrl?: string, pin?: string, allowedMovieRatings: string[] = [], allowedTvRatings: string[] = [], allowedMusicRatings: string[] = [], hasAllLibraryAccess = true, blockUnrated = false, allowedLibs: string[] = [], hasAllIptv = true, allowedIptv: string[] = [], schedules: ProfileScheduleVM[] = [], canRecordLiveTv = false, canAddCustomPodcastFeeds = true, serverId?: string): Promise<void> => {
        await apiClient.post(`/users/${userId}/profiles`, {
            name, profileImageUrl, pin,
            allowedMovieRatings, allowedTvRatings, allowedMusicRatings,
            hasAllLibraryAccess,
            blockUnratedContent: blockUnrated, allowedLibraryIds: allowedLibs,
            hasAllIptvAccess: hasAllIptv, allowedIptvPlaylistIds: allowedIptv,
            accessSchedules: schedules, canRecordLiveTv, canAddCustomPodcastFeeds
        }, { serverId });
    },

    updateProfile: async (profileId: string, name: string, profileImageUrl?: string, pin?: string | null, allowedMovieRatings: string[] = [], allowedTvRatings: string[] = [], allowedMusicRatings: string[] = [], hasAllLibraryAccess = true, blockUnrated = false, allowedLibs: string[] = [], hasAllIptv = true, allowedIptv: string[] = [], schedules: ProfileScheduleVM[] = [], canRecordLiveTv = false, canAddCustomPodcastFeeds = true, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}`, {
            name, profileImageUrl, pin,
            allowedMovieRatings, allowedTvRatings, allowedMusicRatings,
            hasAllLibraryAccess,
            blockUnratedContent: blockUnrated, allowedLibraryIds: allowedLibs,
            hasAllIptvAccess: hasAllIptv, allowedIptvPlaylistIds: allowedIptv,
            accessSchedules: schedules, canRecordLiveTv, canAddCustomPodcastFeeds
        }, { serverId });
    },

    deleteProfile: async (profileId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/users/profiles/${profileId}`, { serverId });
    },

    validatePin: async (profileId: string, pin: string, serverId?: string): Promise<boolean> => {
        try {
            await apiClient.post(`/users/profiles/${profileId}/validate-pin`, { pin }, { serverId });
            return true;
        } catch {
            return false;
        }
    },

    validatePinWithToken: async (baseUrl: string, token: string, profileId: string, pin: string): Promise<void> => {
        const client = createDirectClient(baseUrl, token);
        await client.post(`/users/profiles/${profileId}/validate-pin`, { pin });
    }
};
