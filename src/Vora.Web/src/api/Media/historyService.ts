import { apiClient } from '../client';

export interface HistorySessionDto {
    id: string;
    date: string;
    userName: string;
    accountName?: string;
    ipAddress: string;
    location?: string | null;
    platform: string;
    product: string;
    player: string;
    title: string;
    showTitle?: string;
    seasonNumber?: number;
    episodeNumber?: number;
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

export type HistoryDelivery = 'DirectPlay' | 'DirectStream' | 'Transcode';

export interface HistoryQuery {
    page: number;
    pageSize: number;
    search: string;
    profileId?: string;
    libraryIds?: string[];
    deliveries?: HistoryDelivery[];
}

export const historyService = {
    getHistory: async (query: HistoryQuery, serverId?: string): Promise<{ data: HistorySessionDto[], total: number }> => {
        const response = await apiClient.get<{ data: HistorySessionDto[], total: number }>('/streaming/admin/history', {
            params: {
                page: query.page,
                pageSize: query.pageSize,
                search: query.search,
                profileId: query.profileId || undefined,
                libraryIds: query.libraryIds?.join(',') || undefined,
                streams: query.deliveries?.join(',') || undefined,
            },
            serverId
        });
        return response.data;
    }
};