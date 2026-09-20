import { apiClient } from '../client';

export interface BackgroundTask {
    id: string;
    name: string;
    status: string;
    progress?: string | null;
}

export interface BackgroundTaskPage {
    items: BackgroundTask[];
    total: number;
    running: number;
    skip: number;
    take: number;
}

export const taskService = {
    // A first library scan queues one task per file, so the queue is read a
    // window at a time rather than in full on every update.
    getTasks: async (skip: number, take: number, serverId?: string): Promise<BackgroundTaskPage> => {
        const response = await apiClient.get<BackgroundTaskPage>('/tasks', { params: { skip, take }, serverId });
        return response.data;
    },

    cancelTask: async (taskId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/tasks/${taskId}`, { serverId });
    }
};