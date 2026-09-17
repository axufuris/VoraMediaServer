import { apiClient } from '../client';

export interface BackgroundTask {
    id: string;
    name: string;
    status: string;
    progress?: string | null;
}

export const taskService = {
    getTasks: async (serverId?: string): Promise<BackgroundTask[]> => {
        const response = await apiClient.get<BackgroundTask[]>('/tasks', { serverId });
        return response.data;
    },

    cancelTask: async (taskId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/tasks/${taskId}`, { serverId });
    }
};