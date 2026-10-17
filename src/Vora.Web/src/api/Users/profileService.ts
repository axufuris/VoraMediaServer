import { apiClient } from '../client';

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
    showtimesLocation?: string | null;
    // The profile's own opt-out from AI playlists; true unless switched off.
    aiMusicPlaylistsEnabled?: boolean;
}

export interface ShowtimesLocationDto {
    location?: string | null;
}

export interface PlaybackPreferencesVM {
    autoSkipIntro: boolean;
    autoSkipCredits: boolean;
    autoPlayNext: boolean;
    minimumCreditsSceneSeconds: number;
}

export type LibrarySortField =
    | 'Title'
    | 'ReleaseDate'
    | 'DateAdded'
    | 'AdminRating'
    | 'MyRating'
    | 'CriticRating'
    | 'AudienceRating'
    | 'ContentRating'
    | 'Duration'
    | 'Resolution';

export interface LibrarySortVM {
    sort: LibrarySortField | null;
    descending: boolean;
}

export type SubtitleMode = 'Default' | 'Smart' | 'ForcedOnly' | 'Always' | 'Off';

export interface TrackPreferencesVM {
    audioLanguage?: string | null;
    subtitleLanguage?: string | null;
    subtitleMode?: SubtitleMode | null;
    preferHearingImpairedSubtitles: boolean;
    serverLanguage: string;
    serverSubtitleMode: SubtitleMode;
}

export interface SaveTrackPreferencesRequest {
    audioLanguage: string | null;
    subtitleLanguage: string | null;
    subtitleMode: SubtitleMode | null;
    preferHearingImpairedSubtitles: boolean;
}

export const profileService = {
    // Account owner only. Off means nothing about this profile's listening is
    // sent to OpenAI, and no one can Blend with it.
    setAiPlaylistsEnabled: async (profileId: string, enabled: boolean, serverId?: string): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}/ai-playlists`, { enabled }, { serverId });
    },
    createProfile: async (userId: string, name: string, profileImageUrl?: string, pin?: string, allowedMovieRatings: string[] = [], allowedTvRatings: string[] = [], allowedMusicRatings: string[] = [], hasAllLibraryAccess = true, blockUnrated = false, allowedLibs: string[] = [], hasAllIptv = true, allowedIptv: string[] = [], schedules: ProfileScheduleVM[] = [], canRecordLiveTv = false, canAddCustomPodcastFeeds = true, serverId?: string, showtimesLocation: string | null = null): Promise<void> => {
        await apiClient.post(`/users/${userId}/profiles`, {
            name, profileImageUrl, pin,
            allowedMovieRatings, allowedTvRatings, allowedMusicRatings,
            hasAllLibraryAccess,
            blockUnratedContent: blockUnrated, allowedLibraryIds: allowedLibs,
            hasAllIptvAccess: hasAllIptv, allowedIptvPlaylistIds: allowedIptv,
            accessSchedules: schedules, canRecordLiveTv, canAddCustomPodcastFeeds,
            showtimesLocation
        }, { serverId });
    },

    updateProfile: async (profileId: string, name: string, profileImageUrl?: string, pin?: string | null, allowedMovieRatings: string[] = [], allowedTvRatings: string[] = [], allowedMusicRatings: string[] = [], hasAllLibraryAccess = true, blockUnrated = false, allowedLibs: string[] = [], hasAllIptv = true, allowedIptv: string[] = [], schedules: ProfileScheduleVM[] = [], canRecordLiveTv = false, canAddCustomPodcastFeeds = true, serverId?: string, showtimesLocation: string | null = null): Promise<void> => {
        await apiClient.put(`/users/profiles/${profileId}`, {
            name, profileImageUrl, pin,
            allowedMovieRatings, allowedTvRatings, allowedMusicRatings,
            hasAllLibraryAccess,
            blockUnratedContent: blockUnrated, allowedLibraryIds: allowedLibs,
            hasAllIptvAccess: hasAllIptv, allowedIptvPlaylistIds: allowedIptv,
            accessSchedules: schedules, canRecordLiveTv, canAddCustomPodcastFeeds,
            showtimesLocation
        }, { serverId });
    },

    deleteProfile: async (profileId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/users/profiles/${profileId}`, { serverId });
    },

    getMyShowtimesLocation: async (serverId?: string): Promise<ShowtimesLocationDto> => {
        const response = await apiClient.get<ShowtimesLocationDto>('/users/profiles/me/showtimes-location', { serverId });
        return response.data;
    },

    saveMyShowtimesLocation: async (location: string | null, serverId?: string): Promise<ShowtimesLocationDto> => {
        const response = await apiClient.put<ShowtimesLocationDto, ShowtimesLocationDto>(
            '/users/profiles/me/showtimes-location',
            { location },
            { serverId }
        );
        return response.data;
    },

    adminSetShowtimesLocation: async (profileId: string, location: string | null, serverId?: string): Promise<ShowtimesLocationDto> => {
        const response = await apiClient.put<ShowtimesLocationDto, ShowtimesLocationDto>(
            `/users/profiles/${profileId}/showtimes-location`,
            { location },
            { serverId }
        );
        return response.data;
    },

    getMyPlaybackPreferences: async (serverId?: string): Promise<PlaybackPreferencesVM> => {
        const response = await apiClient.get<PlaybackPreferencesVM>('/users/profiles/me/playback-preferences', { serverId });
        return response.data;
    },

    saveMyPlaybackPreferences: async (prefs: PlaybackPreferencesVM, serverId?: string): Promise<PlaybackPreferencesVM> => {
        const response = await apiClient.put<PlaybackPreferencesVM, PlaybackPreferencesVM>(
            '/users/profiles/me/playback-preferences',
            prefs,
            { serverId }
        );
        return response.data;
    },

    getMyLibrarySort: async (serverId?: string): Promise<LibrarySortVM> => {
        const response = await apiClient.get<LibrarySortVM>('/users/profiles/me/library-sort', { serverId });
        return response.data;
    },

    saveMyLibrarySort: async (sort: LibrarySortVM, serverId?: string): Promise<LibrarySortVM> => {
        const response = await apiClient.put<LibrarySortVM, LibrarySortVM>('/users/profiles/me/library-sort', sort, { serverId });
        return response.data;
    },

    getMyTrackPreferences: async (serverId?: string): Promise<TrackPreferencesVM> => {
        const response = await apiClient.get<TrackPreferencesVM>('/users/profiles/me/track-preferences', { serverId });
        return response.data;
    },

    saveMyTrackPreferences: async (prefs: SaveTrackPreferencesRequest, serverId?: string): Promise<TrackPreferencesVM> => {
        const response = await apiClient.put<TrackPreferencesVM, SaveTrackPreferencesRequest>(
            '/users/profiles/me/track-preferences',
            prefs,
            { serverId }
        );
        return response.data;
    }
};
