import { apiClient } from '../client';

export interface AiUsageLogVM {
    id: string;
    profileName: string;
    timestamp: string;
    pluginId: string;
    modelUsed: string;
    promptTokens: number;
    completionTokens: number;
    totalTokens: number;
}

export interface DailyAiStatVM {
    date: string;
    modelUsed: string;
    promptTokens: number;
    completionTokens: number;
}

export interface AiStatsDashboardVM {
    dailyStats: DailyAiStatVM[];
    logs: AiUsageLogVM[];
    totalLogs: number;
}

export const aiStatsService = {
    getDashboard: async (startDate?: string, endDate?: string, page: number = 1, pageSize: number = 50, serverId?: string): Promise<AiStatsDashboardVM> => {
        const response = await apiClient.get<AiStatsDashboardVM>('/admin/ai-stats', {
            params: { startDate, endDate, page, pageSize },
            serverId
        });
        return response.data;
    },

    triggerAiTask: async (serverId?: string): Promise<void> => {
        await apiClient.post('/admin/ai-stats/trigger', null, { serverId });
    }
};