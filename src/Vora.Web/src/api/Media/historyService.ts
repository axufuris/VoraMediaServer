import { apiClient } from '../client';

export interface HistorySessionDto {
    id: string;
    date: string;
    userName: string;
    ipAddress: string;
    platform: string;
    product: string;
    player: string;
    title: string;
    mediaType: string;
    libraryId: string;
    libraryName: string;

    strategy: string;
    videoStrategy: string;
    audioStrategy: string;

    originalVideoCodec?: string;
    videoCodec?: string;
    originalAudioCodec?: string;
    audioCodec?: string;
    originalAudioChannels?: number;
    targetAudioChannels?: number;
    subtitleStrategy?: string;
    originalSubtitleCodec?: string;
    bandwidthKbps: number;
    sourceResolution?: string;
    sourceHdrType?: string;
    outputResolution?: string;
    outputHdrType?: string;
    decisionLog?: string;

    startedAt: string;
    pausedMinutes: number;
    stoppedAt: string;
    durationMinutes: number;
    percentComplete: number;
    isGrouped: boolean;
    subSessions?: HistorySessionDto[];
}

export const historyService = {
    getHistory: async (page: number, pageSize: number, search: string, serverId?: string): Promise<{ data: HistorySessionDto[], total: number }> => {
        const response = await apiClient.get<{ data: HistorySessionDto[], total: number }>('/streaming/admin/history', {
            params: { page, pageSize, search },
            serverId
        });
        return response.data;
    }
};