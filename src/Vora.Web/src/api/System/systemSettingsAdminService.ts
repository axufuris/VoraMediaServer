import { apiClient } from '../client';

export interface ServerSettings {
    serverName: string;
    enableNightlyScan: boolean;
    nightlyScanTime: string;
    runDetections: number;
    detectionScheduleTime: string;
    silenceThresholdOffsetDb: number;
    silenceMinDurationMovieSec: number;
    silenceMinDurationEpisodeSec: number;
    blackFrameMinDurationSec: number;
    episodeIntroClusterToleranceSec: number;
    episodeIntroClusterMinAgreementPct: number;
    videoThumbnailScheduleTime: string;
    videoThumbnailIntervalSeconds: number;
    videoThumbnailWidth: number;
    videoThumbnailHeight: number;
    videoThumbnailJpegQuality: number;
    videoThumbnailSpriteColumns: number;
    folderWatcherProviderId: string;
    folderWatcherPollingInterval: number;
    localMediaScannerProviderId: string;
    enableTrashAutoPurge: boolean;
    missingMediaRetentionDays: number;
    registrationMode: number;
    internetUploadSpeedMbps: number;
    maxRemoteStreamBitrateMbps: number;
    transcodeQuality: number;
    transcoderTempDirectory: string;
    backgroundX264Preset: number;
    enableHdrToneMapping: boolean;
    disableVideoTranscoding: boolean;
    useHardwareAcceleration: boolean;
    useHardwareEncoding: boolean;
    enableHevcEncoding: number;
    enableHevcOptimization: boolean;
    maxGpuTranscodes: number;
    maxCpuTranscodes: number;
    maxBackgroundTranscodes: number;
    hardwareTranscodingDevice: string;
    transcoderThrottleBuffer: number;
    tonemappingAlgorithm: string;
    streamingProfile: number;
    cacheSizeLimitMb: number;
    enableDailyMixes: boolean;
    dailyMixSchedule: string;
    dailyMixCount: number;
    dailyMixSize: number;
    dailyMixDriftPercent: number;
    dailyMixMinPlays: number;
    dailyMixLastRefreshedAt?: string;
    enableWeeklyMixes: boolean;
    weeklyMixLastRefreshedAt?: string;
    dvrStoragePath?: string | null;
    dvrMaxStorageGb: number;
    dvrStorageWarningPercent: number;
    dvrAutoDeleteWatchedDays: number;
    dvrDefaultSeriesRetention: number;
    dvrNotifyOnFailure: boolean;
    dvrNotifyOnStorageThreshold: boolean;
    dvrPreRollSeconds: number;
    dvrPostRollSeconds: number;
    dvrConflictPolicy: string;
    timeshiftMaxSessionHours: number;
}

export interface PluginSettingField {
    key: string;
    label: string;
    type: string;
    description: string;
    value: string;
}

export const systemSettingsAdminService = {
    getServerSettings: async (serverId?: string): Promise<ServerSettings> => {
        const response = await apiClient.get<ServerSettings>('/settings/server', { serverId });
        return response.data;
    },
    getHardwareDevices: async (serverId?: string): Promise<string[]> => {
        const response = await apiClient.get<string[]>('/settings/hardware-devices', { serverId });
        return response.data;
    },
    updateServerSettings: async (settings: ServerSettings, serverId?: string): Promise<void> => {
        await apiClient.put('/settings/server', settings, { serverId });
    },
    getPluginSettings: async (pluginId: string, serverId?: string): Promise<PluginSettingField[]> => {
        const response = await apiClient.get<PluginSettingField[]>(`/settings/plugins/${pluginId}`, { serverId });
        return response.data;
    },
    updatePluginSettings: async (pluginId: string, settings: Record<string, string>, serverId?: string): Promise<void> => {
        await apiClient.put(`/settings/plugins/${pluginId}`, settings, { serverId });
    }
};
