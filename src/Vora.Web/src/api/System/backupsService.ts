import { apiClient } from '../client';

export type BackupCadence = 'Off' | 'Daily' | 'Weekly' | 'Monthly';
export type DayOfWeekName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

export interface BackupSummaryVM {
    fileName: string;
    createdAtUtc: string;
    fileSizeBytes: number;
    sectionCount: number;
    reason: string;
    voraServerVersion?: string | null;
    manifestReadable: boolean;
}

export interface BackupSectionVM {
    key: string;
    displayName: string;
    group: string;
    requiresExplicitConfirm: boolean;
    destructiveWarning?: string | null;
    sizeBytes: number;
    itemCount?: number | null;
}

export interface BackupManifestVM {
    fileName: string;
    schemaVersion: number;
    voraServerVersion: string;
    createdAtUtc: string;
    kind: string;
    reason: string;
    totalSizeBytes: number;
    sections: BackupSectionVM[];
}

export interface AvailableSectionVM {
    key: string;
    displayName: string;
    group: string;
    requiresExplicitConfirm: boolean;
    destructiveWarning?: string | null;
}

export interface BackupSettingsVM {
    autoBackupEnabled: boolean;
    cadence: BackupCadence;
    hour: number;
    minute: number;
    dayOfWeek: DayOfWeekName;
    dayOfMonth: number;
    maxToKeep: number;
    overrideDirectory?: string | null;
    effectiveDirectory: string;
    lastSuccessfulRunUtc?: string | null;
    nextScheduledRunUtc?: string | null;
    includedSectionKeys?: string[] | null;
    availableSections: AvailableSectionVM[];
}

export interface RestoreSectionResult {
    key: string;
    restored: boolean;
    rowsImported: number;
    rowsSkipped: number;
    warnings: string[];
    error?: string | null;
}

export interface RestoreBackupResult {
    success: boolean;
    error?: string | null;
    sections: RestoreSectionResult[];
}

export interface RestoreBackupRequest {
    sectionKeys: string[];
    acknowledgeAdminLoss: boolean;
}

export const backupsService = {
    list: async (serverId?: string): Promise<BackupSummaryVM[]> => {
        const response = await apiClient.get<BackupSummaryVM[]>('/admin/backups', { serverId });
        return response.data;
    },

    create: async (reason: string, serverId?: string): Promise<BackupSummaryVM> => {
        const response = await apiClient.post<BackupSummaryVM, { reason: string }>('/admin/backups', { reason }, { serverId });
        return response.data;
    },

    getSections: async (serverId?: string): Promise<AvailableSectionVM[]> => {
        const response = await apiClient.get<AvailableSectionVM[]>('/admin/backups/sections', { serverId });
        return response.data;
    },

    getSettings: async (serverId?: string): Promise<BackupSettingsVM> => {
        const response = await apiClient.get<BackupSettingsVM>('/admin/backups/settings', { serverId });
        return response.data;
    },

    updateSettings: async (settings: BackupSettingsVM, serverId?: string): Promise<BackupSettingsVM> => {
        const response = await apiClient.put<BackupSettingsVM, BackupSettingsVM>('/admin/backups/settings', settings, { serverId });
        return response.data;
    },

    getManifest: async (fileName: string, serverId?: string): Promise<BackupManifestVM> => {
        const response = await apiClient.get<BackupManifestVM>(`/admin/backups/${encodeURIComponent(fileName)}/manifest`, { serverId });
        return response.data;
    },

    restore: async (fileName: string, request: RestoreBackupRequest, serverId?: string): Promise<RestoreBackupResult> => {
        const response = await apiClient.post<RestoreBackupResult, RestoreBackupRequest>(
            `/admin/backups/${encodeURIComponent(fileName)}/restore`,
            request,
            { serverId }
        );
        return response.data;
    },

    download: async (fileName: string, serverId?: string): Promise<Blob> => {
        const response = await apiClient.get<Blob>(`/admin/backups/${encodeURIComponent(fileName)}/download`, {
            serverId,
            responseType: 'blob'
        });
        return response.data;
    },

    deleteBackup: async (fileName: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/backups/${encodeURIComponent(fileName)}`, { serverId });
    },

    upload: async (file: File, serverId?: string): Promise<BackupSummaryVM> => {
        const form = new FormData();
        form.append('file', file);
        const response = await apiClient.post<BackupSummaryVM, FormData>('/admin/backups/upload', form, {
            serverId,
            headers: { 'Content-Type': 'multipart/form-data' }
        });
        return response.data;
    }
};
