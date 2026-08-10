import { apiClient } from '../client';

export interface DedupeItemVM {
    partId: string;
    filePath: string;
    fileName: string;
    fileSizeBytes: number;
    videoCodec: string;
    source?: string | null;
    hdrFormat: string;
    audioCodec?: string | null;
    sampleRate?: number | null;
    audioTracks: string[];
    qualityScore: number;
    container: string;
    bitrate?: number;
}

export interface DedupeGroupVM {
    mediaItemId: string;
    title: string;
    type: string;
    mediaKind: 'video' | 'audio';
    resolution: string;
    parts: DedupeItemVM[];
}

export interface DedupeSettingsVM {
    libraryId?: string | null;
    groupAcrossResolutions: boolean;
    runtimeToleranceSeconds: number;
    minimumFileSizeBytes: number;
    minimumRuntimeSeconds: number;
    scoreResolution4k: number;
    scoreResolution1080: number;
    scoreResolution720: number;
    scoreResolutionOther: number;
    scoreSourceRemux: number;
    scoreSourceBluRay: number;
    scoreSourceWebDl: number;
    scoreSourceWebRip: number;
    scoreSourceHdtv: number;
    scoreSourceDvd: number;
    scoreCodecAv1: number;
    scoreCodecHevc: number;
    scoreCodecVp9: number;
    scoreCodecH264: number;
    scoreHdrDolbyVision: number;
    scoreHdr: number;
    scoreHdr10PlusBonus: number;
    scoreAudioLossless: number;
    scoreAudioSurround: number;
    scoreAudioBase: number;
    scoreBitrateDivisor: number;
    scoreCodecMusicLossless: number;
    scoreCodecMusicLossyHigh: number;
    scoreCodecMusicLossyStandard: number;
    scoreSampleRateHi: number;
    scoreSampleRateStandard: number;
    scoreSampleRateLow: number;
    scoreFileSizeDivisor: number;
    isDefault: boolean;
}

export interface DedupeIgnoredGroupVM {
    id: string;
    mediaItemId: string;
    title: string;
    type: string;
    resolution: string;
    ignoredAt: string;
    note?: string;
}

export const adminService = {
    getDuplicates: async (serverId?: string): Promise<DedupeGroupVM[]> => {
        const response = await apiClient.get<DedupeGroupVM[]>('/admin/dedupe', { serverId });
        return response.data;
    },

    deleteDuplicate: async (partId: string, deletePhysical: boolean, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/dedupe/${partId}`, {
            params: { deletePhysical },
            serverId
        });
    },

    getDedupeSettings: async (serverId?: string): Promise<DedupeSettingsVM> => {
        const response = await apiClient.get<DedupeSettingsVM>('/admin/dedupe/settings', { serverId });
        return response.data;
    },

    updateDedupeSettings: async (settings: DedupeSettingsVM, serverId?: string): Promise<DedupeSettingsVM> => {
        const response = await apiClient.put<DedupeSettingsVM, DedupeSettingsVM>('/admin/dedupe/settings', settings, { serverId });
        return response.data;
    },

    getDedupeDefaults: async (serverId?: string): Promise<DedupeSettingsVM> => {
        const response = await apiClient.get<DedupeSettingsVM>('/admin/dedupe/settings/defaults', { serverId });
        return response.data;
    },

    getLibraryDedupeSettings: async (libraryId: string, serverId?: string): Promise<DedupeSettingsVM> => {
        const response = await apiClient.get<DedupeSettingsVM>(`/admin/dedupe/settings/library/${libraryId}`, { serverId });
        return response.data;
    },

    updateLibraryDedupeSettings: async (libraryId: string, settings: DedupeSettingsVM, serverId?: string): Promise<DedupeSettingsVM> => {
        const response = await apiClient.put<DedupeSettingsVM, DedupeSettingsVM>(`/admin/dedupe/settings/library/${libraryId}`, settings, { serverId });
        return response.data;
    },

    clearLibraryDedupeSettings: async (libraryId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/dedupe/settings/library/${libraryId}`, { serverId });
    },

    getLibraryDedupeOverrides: async (serverId?: string): Promise<DedupeSettingsVM[]> => {
        const response = await apiClient.get<DedupeSettingsVM[]>('/admin/dedupe/settings/library-overrides', { serverId });
        return response.data;
    },

    getIgnoredDuplicates: async (serverId?: string): Promise<DedupeIgnoredGroupVM[]> => {
        const response = await apiClient.get<DedupeIgnoredGroupVM[]>('/admin/dedupe/ignored', { serverId });
        return response.data;
    },

    ignoreDuplicateGroup: async (mediaItemId: string, resolution: string, note?: string, serverId?: string): Promise<void> => {
        await apiClient.post('/admin/dedupe/ignored', { mediaItemId, resolution, note }, { serverId });
    },

    unignoreDuplicateGroup: async (ignoredGroupId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/admin/dedupe/ignored/${ignoredGroupId}`, { serverId });
    }
};
