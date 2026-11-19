import { apiClient } from '../client';

export type WebhookLibraryKind = 'Movie' | 'TvShow' | 'Music';

export interface WebhookProviderVM {
    id: string;
    name: string;
    description: string;
    libraryKind: WebhookLibraryKind;
    receivePath: string;
    isConfigured: boolean;
    isEnabled: boolean;
}

export interface WebhookDeliveryVM {
    id: string;
    at: string;
    providerId: string;
    instance: string | null;
    event: string;
    path: string;
    outcome: string;
    ok: boolean;
}

export interface GeneratedWebhookSecret {
    secret: string;
}

export const webhookAdminService = {
    getProviders: async (serverId?: string): Promise<WebhookProviderVM[]> => {
        const response = await apiClient.get<WebhookProviderVM[]>('/admin/webhooks', { serverId });
        return response.data;
    },

    getDeliveries: async (limit = 100, serverId?: string): Promise<WebhookDeliveryVM[]> => {
        const response = await apiClient.get<WebhookDeliveryVM[]>('/admin/webhooks/deliveries', { params: { limit }, serverId });
        return response.data;
    },

    generateSecret: async (providerId: string, serverId?: string): Promise<GeneratedWebhookSecret> => {
        const response = await apiClient.post<GeneratedWebhookSecret>(`/admin/webhooks/${encodeURIComponent(providerId)}/secret`, undefined, { serverId });
        return response.data;
    }
};
