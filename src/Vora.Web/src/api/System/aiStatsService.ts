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

// Friendly names for the AI plugins that log usage, so the log shows which
// feature made each call rather than only the raw model.
export const AI_FEATURE_OPTIONS: { id: string; label: string }[] = [
    { id: 'openai_recommendations', label: 'Recommendations' },
    { id: 'openai_chronology', label: 'Collection Ordering' },
    { id: 'openai_list', label: 'Collection List' },
];

export function aiFeatureLabel(pluginId: string): string {
    return AI_FEATURE_OPTIONS.find(o => o.id === pluginId)?.label ?? pluginId;
}

export const aiStatsService = {
    getDashboard: async (startDate?: string, endDate?: string, page: number = 1, pageSize: number = 50, pluginId?: string, serverId?: string): Promise<AiStatsDashboardVM> => {
        const response = await apiClient.get<AiStatsDashboardVM>('/admin/ai-stats', {
            params: { startDate, endDate, page, pageSize, pluginId: pluginId || undefined },
            serverId
        });
        return response.data;
    },

    triggerAiTask: async (serverId?: string): Promise<void> => {
        await apiClient.post('/admin/ai-stats/trigger', null, { serverId });
    }
};