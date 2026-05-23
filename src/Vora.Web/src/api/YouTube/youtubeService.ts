import { apiClient } from '../client';

export type YouTubeAccessSetting = 'Inherit' | 'Enabled' | 'Disabled';

export interface YouTubeVideo {
    videoId: string;
    title: string;
    description?: string;
    thumbnailUrl: string;
    channelId: string;
    channelName: string;
    publishedAt?: string;
    viewCount?: number;
    durationSeconds?: number;
    embedWidth?: number;
    embedHeight?: number;
}

export interface YouTubeContinueWatching {
    videoId: string;
    title: string;
    thumbnailUrl: string;
    channelId: string;
    channelName: string;
    durationWatched: number;
    totalDuration: number;
    percentComplete: number;
    watchedAt: string;
}

export interface YouTubeSearchPage {
    videos: YouTubeVideo[];
    nextPageToken?: string;
}

export interface YouTubeHomeFeed {
    continueWatching: YouTubeContinueWatching[];
    fromSubscriptions: YouTubeVideo[];
    trending: YouTubeVideo[];
    recommendedForYou: YouTubeVideo[];
    isFreshState: boolean;
}

export interface YouTubePlaylist {
    playlistId: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    itemCount?: number;
    publishedAt?: string;
    youTubeUrl: string;
}

export interface YouTubeChannel {
    channelId: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    subscriberCount?: number;
    videoCount?: number;
    isSubscribed: boolean;
    recentUploads: YouTubeVideo[];
}

export interface YouTubeSubscription {
    channelId: string;
    channelName: string;
    channelThumbnailUrl?: string;
    subscribedAt: string;
}

export interface YouTubeWatchHistoryEntry {
    videoId: string;
    videoTitle: string;
    thumbnailUrl: string;
    channelId: string;
    channelName: string;
    durationWatched: number;
    totalDuration: number;
    watchedAt: string;
}

export interface YouTubeProfileSettings {
    isEnabled: boolean;
    isAvailable: boolean;
    unavailableReason?: string;
}

export interface YouTubeAccountSettings {
    accountId: string;
    youTubeAccess: YouTubeAccessSetting;
    updatedAt?: string;
}

export interface YouTubeStatus {
    pluginInstalled: boolean;
    apiKeyConfigured: boolean;
    serverEnabled: boolean;
    trendingRegion: string;
}

export interface RecordWatchHistoryInput {
    videoId: string;
    videoTitle: string;
    thumbnailUrl: string;
    channelId: string;
    channelName: string;
    durationWatched: number;
    totalDuration: number;
}

export const youtubeService = {
    getHomeFeed: async (serverId?: string): Promise<YouTubeHomeFeed> => {
        const response = await apiClient.get<YouTubeHomeFeed>('/youtube/feed', { serverId });
        return response.data;
    },
    getTrending: async (serverId?: string): Promise<YouTubeVideo[]> => {
        const response = await apiClient.get<YouTubeVideo[]>('/youtube/trending', { serverId });
        return response.data;
    },
    search: async (query: string, pageToken?: string, serverId?: string): Promise<YouTubeSearchPage> => {
        const tokenParam = pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : '';
        const response = await apiClient.get<YouTubeSearchPage>(`/youtube/search?q=${encodeURIComponent(query)}${tokenParam}`, { serverId });
        return response.data;
    },
    getChannel: async (channelId: string, serverId?: string): Promise<YouTubeChannel> => {
        const response = await apiClient.get<YouTubeChannel>(`/youtube/channel/${encodeURIComponent(channelId)}`, { serverId });
        return response.data;
    },
    getChannelUploads: async (channelId: string, pageToken?: string, serverId?: string): Promise<YouTubeSearchPage> => {
        const tokenParam = pageToken ? `?pageToken=${encodeURIComponent(pageToken)}` : '';
        const response = await apiClient.get<YouTubeSearchPage>(`/youtube/channel/${encodeURIComponent(channelId)}/uploads${tokenParam}`, { serverId });
        return response.data;
    },
    getChannelPlaylists: async (channelId: string, serverId?: string): Promise<YouTubePlaylist[]> => {
        const response = await apiClient.get<YouTubePlaylist[]>(`/youtube/channel/${encodeURIComponent(channelId)}/playlists`, { serverId });
        return response.data;
    },
    getVideo: async (videoId: string, serverId?: string): Promise<YouTubeVideo> => {
        const response = await apiClient.get<YouTubeVideo>(`/youtube/video/${encodeURIComponent(videoId)}`, { serverId });
        return response.data;
    },

    getSubscriptions: async (serverId?: string): Promise<YouTubeSubscription[]> => {
        const response = await apiClient.get<YouTubeSubscription[]>('/youtube/subscriptions', { serverId });
        return response.data;
    },
    subscribe: async (channelId: string, serverId?: string): Promise<YouTubeSubscription> => {
        const response = await apiClient.post<YouTubeSubscription>('/youtube/subscriptions', { channelId }, { serverId });
        return response.data;
    },
    unsubscribe: async (channelId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/youtube/subscriptions/${encodeURIComponent(channelId)}`, { serverId });
    },

    getHistory: async (serverId?: string): Promise<YouTubeWatchHistoryEntry[]> => {
        const response = await apiClient.get<YouTubeWatchHistoryEntry[]>('/youtube/history', { serverId });
        return response.data;
    },
    recordWatch: async (entry: RecordWatchHistoryInput, serverId?: string): Promise<void> => {
        await apiClient.post('/youtube/history', entry, { serverId });
    },
    clearHistory: async (serverId?: string): Promise<void> => {
        await apiClient.delete('/youtube/history', { serverId });
    },

    getProfileSettings: async (serverId?: string): Promise<YouTubeProfileSettings> => {
        const response = await apiClient.get<YouTubeProfileSettings>('/youtube/settings', { serverId });
        return response.data;
    },
    updateProfileSettings: async (isEnabled: boolean, serverId?: string): Promise<YouTubeProfileSettings> => {
        const response = await apiClient.put<YouTubeProfileSettings>('/youtube/settings', { isEnabled }, { serverId });
        return response.data;
    },

    getAdminStatus: async (serverId?: string): Promise<YouTubeStatus> => {
        const response = await apiClient.get<YouTubeStatus>('/admin/youtube/status', { serverId });
        return response.data;
    },
    getAccountSettings: async (accountId: string, serverId?: string): Promise<YouTubeAccountSettings> => {
        const response = await apiClient.get<YouTubeAccountSettings>(`/admin/youtube/accounts/${accountId}`, { serverId });
        return response.data;
    },
    updateAccountSettings: async (accountId: string, youTubeAccess: YouTubeAccessSetting, serverId?: string): Promise<YouTubeAccountSettings> => {
        const response = await apiClient.put<YouTubeAccountSettings>(`/admin/youtube/accounts/${accountId}`, { youTubeAccess }, { serverId });
        return response.data;
    },
};
