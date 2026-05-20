import { apiClient } from '../client';

export type AdminNotificationSeverity = 'Info' | 'Warning' | 'Error';

export interface AdminNotificationVM {
    id: string;
    createdAt: string;
    severity: AdminNotificationSeverity;
    title: string;
    message: string;
    isRead: boolean;
    contextJson?: string;
}

export interface AdminAlertEvent {
    severity: AdminNotificationSeverity;
    title: string;
    message: string;
    timestamp: string;
}

export const adminNotificationService = {
    getRecent: async (limit?: number, unreadOnly?: boolean, serverId?: string): Promise<AdminNotificationVM[]> => {
        const params: Record<string, string | number | boolean> = {};
        if (limit !== undefined) params.limit = limit;
        if (unreadOnly !== undefined) params.unreadOnly = unreadOnly;
        const response = await apiClient.get<AdminNotificationVM[]>('/admin/notifications', {
            params: Object.keys(params).length > 0 ? params : undefined,
            serverId
        });
        return response.data;
    },

    getUnreadCount: async (serverId?: string): Promise<number> => {
        const response = await apiClient.get<number>('/admin/notifications/unread-count', { serverId });
        return response.data;
    },

    markRead: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.put(`/admin/notifications/${id}/read`, undefined, { serverId });
    },

    markAllRead: async (serverId?: string): Promise<void> => {
        await apiClient.post('/admin/notifications/mark-all-read', undefined, { serverId });
    }
};
