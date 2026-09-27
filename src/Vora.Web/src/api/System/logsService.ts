import { apiClient } from '../client';

export type LogLevel = 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical';

export const ALL_LOG_LEVELS: LogLevel[] = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'];

export interface LogEntryVM {
    id: number;
    timestampUtc: string;
    level: LogLevel;
    category: string;
    eventId: number;
    message: string;
    exception?: string | null;
    hasException: boolean;
}

export interface LogQueryResultVM {
    entries: LogEntryVM[];
    totalMatched: number;
    moreAvailable: boolean;
    oldestId?: number | null;
    newestId?: number | null;
}

export interface LogLevelEntryVM {
    category: string;
    level: LogLevel;
    isOverride: boolean;
}

export interface LogLevelStateVM {
    defaultLevel: LogLevel;
    overrides: LogLevelEntryVM[];
    knownCategories: string[];
}

export interface LogQueryParams {
    levels?: LogLevel[];
    category?: string;
    search?: string;
    sinceUtc?: string;
    untilUtc?: string;
    beforeId?: number;
    limit?: number;
}

function toQuery(params: LogQueryParams): Record<string, string> {
    const q: Record<string, string> = {};
    if (params.levels && params.levels.length > 0) q.levels = params.levels.join(',');
    if (params.category) q.category = params.category;
    if (params.search) q.search = params.search;
    if (params.sinceUtc) q.sinceUtc = params.sinceUtc;
    if (params.untilUtc) q.untilUtc = params.untilUtc;
    if (typeof params.beforeId === 'number') q.beforeId = String(params.beforeId);
    if (typeof params.limit === 'number') q.limit = String(params.limit);
    return q;
}

export const logsService = {
    query: async (params: LogQueryParams, serverId?: string): Promise<LogQueryResultVM> => {
        const response = await apiClient.get<LogQueryResultVM>('/admin/logs', {
            serverId,
            params: toQuery(params)
        });
        return response.data;
    },

    exportLogs: async (format: 'txt' | 'json', params: LogQueryParams, serverId?: string): Promise<Blob> => {
        const response = await apiClient.get<Blob>('/admin/logs/export', {
            serverId,
            params: { ...toQuery(params), format },
            responseType: 'blob'
        });
        return response.data;
    },

    getCategories: async (serverId?: string): Promise<string[]> => {
        const response = await apiClient.get<string[]>('/admin/logs/categories', { serverId });
        return response.data;
    },

    getLevels: async (serverId?: string): Promise<LogLevelStateVM> => {
        const response = await apiClient.get<LogLevelStateVM>('/admin/logs/levels', { serverId });
        return response.data;
    },

    setLevel: async (category: string, level: LogLevel, serverId?: string): Promise<void> => {
        await apiClient.put(`/admin/logs/levels/${encodeURIComponent(category)}`, { level }, { serverId });
    },

    clearLevel: async (category: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/logs/levels/${encodeURIComponent(category)}`, { serverId });
    }
};
