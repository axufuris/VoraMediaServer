import { apiClient } from '../client';

export interface NowPlayingSession {
    sessionId: string;
    mediaId: string;
    title: string;
    tvShowTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    posterUrl?: string;
    durationSeconds: number;
    clientName: string;
    deviceName: string;
    deviceType: string;
    ipAddress: string;
    strategy: string;
    videoStrategy: string;
    audioStrategy: string;
    subtitleStrategy: string;
    container: string;
    videoCodec: string;
    audioCodec: string;
    targetAudioChannels: number;
    quality: string;
    bandwidthKbps: number;
    currentPosition: number;
    isPaused: boolean;
    userName: string;
    resolution?: string;
    hdrType?: string;
    originalContainer?: string;
    originalVideoCodec?: string;
    originalAudioCodec?: string;
    originalAudioChannels?: number;
    originalSubtitleCodec?: string;
    decisionLog?: string;
}

export interface SystemStats {
    cpuUsagePercentage: number;
    ramUsageGb: number;
    diskTotalBytes: number;
    diskUsedBytes: number;
    diskFreeBytes: number;
}

export const streamingAdminService = {
    getNowPlaying: async (serverId?: string) => {
        const res = await apiClient.get<NowPlayingSession[]>('/streaming/admin/now-playing', { serverId });
        return res.data;
    },
    getSystemStats: async (serverId?: string) => {
        const res = await apiClient.get<SystemStats>('/streaming/admin/system-stats', { serverId });
        return res.data;
    },
    sendCommand: async (sessionId: string, command: 'play' | 'pause' | 'stop', message?: string, serverId?: string) => {
        await apiClient.post(`/streaming/admin/sessions/${sessionId}/command`, { command, message }, { serverId });
    }
};