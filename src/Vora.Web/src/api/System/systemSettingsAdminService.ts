import { apiClient } from '../client';

export interface ServerSettings {
    serverName: string;
    scheduleTimeZone: string;
    enableNightlyScan: boolean;
    nightlyScanTime: string;
    scanIgnoredFolders: string[];
    runDetections: number;
    detectionScheduleTime: string;
    silenceThresholdOffsetDb: number;
    silenceMinDurationMovieSec: number;
    silenceMinDurationEpisodeSec: number;
    blackFrameMinDurationSec: number;
    episodeIntroClusterToleranceSec: number;
    episodeIntroClusterMinAgreementPct: number;
    analyzeConcurrency: number;
    analyzeUseHardwareDecode: boolean;
    videoThumbnailGeneration: number;
    videoThumbnailScheduleTime: string;
    iptvHealthCheckTime: string;
    videoThumbnailIntervalSeconds: number;
    videoThumbnailWidth: number;
    videoThumbnailHeight: number;
    videoThumbnailJpegQuality: number;
    videoThumbnailSpriteColumns: number;
    videoThumbnailConcurrency: number;
    videoThumbnailUseHardwareDecode: boolean;
    preExtractSubtitlesOnScan: boolean;
    folderWatcherProviderId: string;
    folderWatcherPollingInterval: number;
    localMediaScannerProviderId: string;
    subtitleSearchProviderId: string;
    enableTrashAutoPurge: boolean;
    missingMediaRetentionDays: number;
    resolveMovieTvdbIds: boolean;
    metadataLanguage: string;
    autoEnableSubtitlesForForeignAudio: boolean;
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
    // AI playlists: off until an admin turns them on. Requests is the "Make me a
    // playlist for..." box, with its own switch and a per-profile daily limit.
    enableAiMusicPlaylists: boolean;
    enableAiPlaylistRequests: boolean;
    aiPlaylistRequestsPerDay: number;
    // Cosine distance past which a song no longer counts as a match. Lower is
    // stricter: shorter playlists that stay closer to what was asked.
    aiPlaylistMatchCutoff: number;
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
    placeholder: string;
    required: boolean;
    options: string[];
}

// The server's RegistrationMode names, by the number the settings page uses.
const REGISTRATION_MODES: Record<number, string> = { 0: 'Disabled', 1: 'Simple', 2: 'SecretWord', 3: 'Invitation' };

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
    // Its own call: the full settings save leaves the registration mode alone,
    // so System Settings can never undo a change made on Users & Access.
    updateRegistrationMode: async (mode: number, serverId?: string): Promise<void> => {
        await apiClient.put('/settings/registration-mode', { mode: REGISTRATION_MODES[mode] ?? 'SecretWord' }, { serverId });
    },
    queueSubtitleBackfill: async (serverId?: string): Promise<void> => {
        await apiClient.post('/metadata/subtitles/backfill', {}, { serverId });
    },
    getPluginSettings: async (pluginId: string, serverId?: string): Promise<PluginSettingField[]> => {
        const response = await apiClient.get<PluginSettingField[]>(`/settings/plugins/${pluginId}`, { serverId });
        return response.data;
    },
    updatePluginSettings: async (pluginId: string, settings: Record<string, string>, serverId?: string): Promise<void> => {
        await apiClient.put(`/settings/plugins/${pluginId}`, settings, { serverId });
    },
    testPluginConnection: async (pluginId: string, settings: Record<string, string>, serverId?: string): Promise<PluginConnectionTestResult> => {
        const response = await apiClient.post<PluginConnectionTestResult>(`/settings/plugins/${pluginId}/test`, settings, { serverId });
        return response.data;
    }
};

export interface PluginConnectionTestResult {
    success: boolean;
    message: string;
}
