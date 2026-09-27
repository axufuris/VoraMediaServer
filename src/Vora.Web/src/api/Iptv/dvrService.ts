import { apiClient } from '../client';

export interface IptvRecordingScheduleVM {
    isSeries?: boolean;
    channel: {
        name: string;
        logoUrl?: string;
    };
}

export interface IptvRecordingSessionVM {
    id: string;
    title: string;
    episodeTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
    startTime: string;
    endTime: string;
    status: string;
    outputFilePath?: string;
    errorMessage?: string;
    commercialMarkersJson?: string;
    fileSizeBytes?: number;
    externalProgramId?: string;
    schedule: {
        isSeries?: boolean;
        channel: {
            name: string;
            logoUrl?: string;
        }
    }
}

export const dvrService = {
    scheduleRecording: async (profileId: string, channelId: string, title: string, programId?: string, isSeries: boolean = false, keepMaxEpisodes: number = 0, serverId?: string): Promise<void> => {
        await apiClient.post('/iptv/dvr/schedule', { profileId, channelId, title, programId, isSeries, keepMaxEpisodes }, { serverId });
    },

    getRecordingSessions: async (profileId: string, serverId?: string): Promise<IptvRecordingSessionVM[]> => {
        const response = await apiClient.get<IptvRecordingSessionVM[]>(`/iptv/dvr/sessions/${profileId}`, { serverId });
        return response.data;
    },

    deleteDvrSession: async (sessionId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/iptv/dvr/sessions/${sessionId}`, { serverId });
    },

    cancelDvrSeries: async (sessionId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/iptv/dvr/series/${sessionId}`, { serverId });
    }
};
