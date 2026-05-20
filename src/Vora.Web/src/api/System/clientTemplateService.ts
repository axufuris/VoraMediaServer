import { apiClient } from '../client';
import type { ThemeManifest } from '../../theme/types';

export interface TemplateMetaVM {
    id: string;
    name: string;
    version: string;
    author?: string;
    description?: string;
    preview?: string;
    isBuiltIn: boolean;
}

export type ActiveTemplateSource = 'Default' | 'Profile' | 'Schedule' | 'Override';

export interface TemplateScheduleVM {
    id: string;
    templateId: string;
    name: string;
    startsAtUtc: string;
    endsAtUtc: string;
    priority: number;
    enabled: boolean;
    templateMissing: boolean;
}

export interface ActiveTemplateVM {
    templateId: string;
    source: ActiveTemplateSource;
    schedule?: TemplateScheduleVM | null;
}

export interface SetActiveTemplateResponse {
    templateId: string;
    source: ActiveTemplateSource;
}

export interface CreateTemplateScheduleRequest {
    templateId: string;
    name: string;
    startsAtUtc: string;
    endsAtUtc: string;
    priority: number;
    enabled: boolean;
}

export type UpdateTemplateScheduleRequest = CreateTemplateScheduleRequest;

export const clientTemplateService = {
    getActive: async (serverId?: string): Promise<ActiveTemplateVM> => {
        const response = await apiClient.get<ActiveTemplateVM>('/templates/active', { serverId });
        return response.data;
    },

    getAll: async (serverId?: string): Promise<TemplateMetaVM[]> => {
        const response = await apiClient.get<TemplateMetaVM[]>('/templates', { serverId });
        return response.data;
    },

    setActive: async (templateId: string, serverId?: string): Promise<SetActiveTemplateResponse> => {
        const response = await apiClient.put<SetActiveTemplateResponse>('/templates/active', { templateId }, { serverId });
        return response.data;
    },

    clearActive: async (serverId?: string): Promise<void> => {
        await apiClient.delete('/templates/active', { serverId });
    },

    getManifest: async (templateId: string, serverId?: string): Promise<ThemeManifest> => {
        const response = await apiClient.get<ThemeManifest>(`/templates/${encodeURIComponent(templateId)}/manifest`, { serverId });
        return response.data;
    },

    getDefault: async (serverId?: string): Promise<string> => {
        const response = await apiClient.get<{ templateId: string }>('/admin/templates/default', { serverId });
        return response.data.templateId;
    },

    setDefault: async (templateId: string, serverId?: string): Promise<void> => {
        await apiClient.put('/admin/templates/default', { templateId }, { serverId });
    },

    rescan: async (serverId?: string): Promise<number> => {
        const response = await apiClient.post<{ bundleCount: number }>('/admin/templates/rescan', undefined, { serverId });
        return response.data.bundleCount;
    },

    getSchedules: async (serverId?: string): Promise<TemplateScheduleVM[]> => {
        const response = await apiClient.get<TemplateScheduleVM[]>('/admin/templates/schedules', { serverId });
        return response.data;
    },

    createSchedule: async (request: CreateTemplateScheduleRequest, serverId?: string): Promise<TemplateScheduleVM> => {
        const response = await apiClient.post<TemplateScheduleVM>('/admin/templates/schedules', request, { serverId });
        return response.data;
    },

    updateSchedule: async (id: string, request: UpdateTemplateScheduleRequest, serverId?: string): Promise<TemplateScheduleVM> => {
        const response = await apiClient.put<TemplateScheduleVM>(`/admin/templates/schedules/${encodeURIComponent(id)}`, request, { serverId });
        return response.data;
    },

    deleteSchedule: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/templates/schedules/${encodeURIComponent(id)}`, { serverId });
    },
};
