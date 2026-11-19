import { apiClient } from '../client';

export type WebhookLibraryKind = 'Movie' | 'TvShow' | 'Music' | 'HomeVideo';
export type WebhookSettingType = 'text' | 'textarea' | 'select' | 'library';
export type WebhookChangeKind = 'Added' | 'Removed' | 'FolderRemoved' | 'ScanLibrary';

export interface WebhookSettingDefinition {
    key: string;
    label: string;
    type: WebhookSettingType;
    defaultValue: string;
    description: string;
    placeholder: string;
    required: boolean;
    options: string[];
    showWhen: string;
}

export interface WebhookTemplateVM {
    id: string;
    name: string;
    description: string;
    defaultConnectionName: string;
    setupInstructions: string;
    libraryKind: WebhookLibraryKind | null;
    requestServerProviderId: string | null;
    settings: WebhookSettingDefinition[];
}

export interface WebhookConnectionVM {
    id: string;
    name: string;
    providerId: string;
    providerName: string;
    isEnabled: boolean;
    handleDeletes: boolean;
    receivePath: string;
    pathMappings: string;
    settings: Record<string, string>;
    createdAt: string;
    lastDeliveryAt: string | null;
    lastDeliveryOk: boolean | null;
}

export interface WebhookConnectionWithSecret extends WebhookConnectionVM {
    secret: string;
}

export interface SaveWebhookConnection {
    name: string;
    isEnabled: boolean;
    handleDeletes: boolean;
    pathMappings: string;
    settings: Record<string, string>;
}

export interface CreateWebhookConnection extends SaveWebhookConnection {
    providerId: string;
}

export interface WebhookDeliveryVM {
    id: string;
    at: string;
    connectionId: string;
    connectionName: string;
    providerId: string;
    instance: string | null;
    event: string;
    path: string;
    outcome: string;
    ok: boolean;
}

export interface WebhookPreviewRequest {
    providerId: string;
    settings: Record<string, string>;
    pathMappings: string;
    body: string;
}

export interface WebhookPreviewChange {
    kind: WebhookChangeKind;
    reportedPath: string;
    mappedPath: string | null;
    libraryName: string | null;
    existsOnDisk: boolean | null;
    note: string;
}

export interface WebhookPreviewResult {
    ok: boolean;
    error: string | null;
    event: string;
    isTest: boolean;
    instance: string | null;
    changes: WebhookPreviewChange[];
}

export interface WebhookRequestServerVM {
    id: string;
    name: string;
    providerId: string;
    webhookProviderId: string;
}

export interface WebhookRequestServerMappings {
    name: string;
    rootFolders: string[];
    mappings: string;
    unmatchedRoots: string[];
    error: string | null;
}

export interface GeneratedWebhookSecret {
    secret: string;
}

const BASE = '/admin/webhooks';

export const webhookAdminService = {
    getProviders: async (serverId?: string): Promise<WebhookTemplateVM[]> => {
        const response = await apiClient.get<WebhookTemplateVM[]>(`${BASE}/providers`, { serverId });
        return response.data;
    },

    getConnections: async (serverId?: string): Promise<WebhookConnectionVM[]> => {
        const response = await apiClient.get<WebhookConnectionVM[]>(`${BASE}/connections`, { serverId });
        return response.data;
    },

    getConnection: async (id: string, serverId?: string): Promise<WebhookConnectionWithSecret> => {
        const response = await apiClient.get<WebhookConnectionWithSecret>(`${BASE}/connections/${encodeURIComponent(id)}`, { serverId });
        return response.data;
    },

    createConnection: async (body: CreateWebhookConnection, serverId?: string): Promise<WebhookConnectionWithSecret> => {
        const response = await apiClient.post<WebhookConnectionWithSecret>(`${BASE}/connections`, body, { serverId });
        return response.data;
    },

    updateConnection: async (id: string, body: SaveWebhookConnection, serverId?: string): Promise<WebhookConnectionVM> => {
        const response = await apiClient.put<WebhookConnectionVM>(`${BASE}/connections/${encodeURIComponent(id)}`, body, { serverId });
        return response.data;
    },

    regenerateSecret: async (id: string, serverId?: string): Promise<GeneratedWebhookSecret> => {
        const response = await apiClient.post<GeneratedWebhookSecret>(`${BASE}/connections/${encodeURIComponent(id)}/secret`, undefined, { serverId });
        return response.data;
    },

    deleteConnection: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`${BASE}/connections/${encodeURIComponent(id)}`, { serverId });
    },

    getDeliveries: async (limit = 100, connectionId?: string, serverId?: string): Promise<WebhookDeliveryVM[]> => {
        const params: Record<string, string | number> = { limit };
        if (connectionId) params.connectionId = connectionId;
        const response = await apiClient.get<WebhookDeliveryVM[]>(`${BASE}/deliveries`, { params, serverId });
        return response.data;
    },

    preview: async (request: WebhookPreviewRequest, serverId?: string): Promise<WebhookPreviewResult> => {
        const response = await apiClient.post<WebhookPreviewResult>(`${BASE}/preview`, request, { serverId });
        return response.data;
    },

    getRequestServers: async (serverId?: string): Promise<WebhookRequestServerVM[]> => {
        const response = await apiClient.get<WebhookRequestServerVM[]>(`${BASE}/request-servers`, { serverId });
        return response.data;
    },

    getRequestServerMappings: async (id: string, serverId?: string): Promise<WebhookRequestServerMappings> => {
        const response = await apiClient.get<WebhookRequestServerMappings>(`${BASE}/request-servers/${encodeURIComponent(id)}/mappings`, { serverId });
        return response.data;
    }
};
