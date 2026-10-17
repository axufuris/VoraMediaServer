import { apiClient } from '../client';
import { scanDeviceCapabilities } from '../../utils/hardwareScanner';

export interface StartSessionResponse {
    sessionId: string;
    streamUrl: string;
    videoTrackId: string;
    audioTrackId: string;
    audioTrackPosition?: number;
    subtitleTrackId: string | null;
    strategy: string;
    videoStrategy: string;
    audioStrategy: string;
    subtitleStrategy: string | null;
    videoCodec: string;
    audioCodec: string;
    targetAudioChannels: number;
    container: string;
    bandwidthKbps: number;
    outputResolution?: string | null;
    outputHdrType?: string | null;
    // Why the server chose this way of playing, one sentence per reason.
    reasons?: string[];
}

export interface PlaybackVideoStats {
    codec?: string | null;
    profile?: string | null;
    level?: string | null;
    bitDepth?: number | null;
    hdrType?: string | null;
    width?: number | null;
    height?: number | null;
    frameRate?: number | null;
    bitrateKbps?: number | null;
    strategy: string;
    outputCodec?: string | null;
    outputWidth?: number | null;
    outputHeight?: number | null;
    outputHdrType?: string | null;
    outputBitrateKbps?: number | null;
    outputPeakKbps?: number | null;
    toneMapping?: string | null;
}

export interface PlaybackAudioStats {
    codec?: string | null;
    profile?: string | null;
    channels?: number | null;
    channelLayout?: string | null;
    sampleRate?: number | null;
    language?: string | null;
    title?: string | null;
    isDefault: boolean;
    bitrateKbps?: number | null;
    strategy: string;
    outputCodec?: string | null;
    outputChannels?: number | null;
    outputBitrateKbps?: number | null;
}

export interface PlaybackSubtitleStats {
    codec?: string | null;
    language?: string | null;
    title?: string | null;
    isForced: boolean;
    isExternal: boolean;
    strategy: string;
}

export interface PlaybackTranscoderStats {
    encoder?: string | null;
    encodeHardware?: string | null;
    decodeHardware?: string | null;
    fps?: number | null;
    speed?: number | null;
    running: boolean;
    retriedOnCpu: boolean;
    // Throttling has it waiting for the player to catch up.
    paused: boolean;
}

// What the server knows about a playing stream: the source beside what the
// viewer gets, and the transcoder's live speed. GET /streaming/sessions/{id}/stats.
export interface PlaybackStats {
    method: string;
    delivery: string;
    sourceContainer?: string | null;
    sourceBitrateKbps?: number | null;
    streamBitrateKbps?: number | null;
    video?: PlaybackVideoStats | null;
    audio?: PlaybackAudioStats | null;
    subtitle?: PlaybackSubtitleStats | null;
    transcoder?: PlaybackTranscoderStats | null;
}

export interface StreamDecisionPreview {
    mediaPartId: string;
    videoTrackId: string;
    audioTrackId: string;
    audioTrackPosition?: number;
    subtitleTrackId: string | null;
    strategy: string;
    videoStrategy: string;
    audioStrategy: string;
    targetAudioChannels: number;
    // The version or track picked (now or remembered) would have had to be
    // converted on this device, so the server plays the closest one that doesn't.
    pickReplaced?: boolean;
}

export type PlaybackChoiceKind = 'Version' | 'Audio' | 'Subtitles';

// What this profile picked for a movie or show on this device. The server
// applies it to every episode by language, edition and format, so it carries
// no track ids.
export interface RememberedChoice {
    remembersVersion: boolean;
    remembersAudio: boolean;
    subtitle: 'Automatic' | 'Off' | 'Language';
}

export interface RememberChoiceChange {
    mediaPartId?: string;
    audioTrackId?: string;
    subtitleTrackId?: string;
    subtitlesOff?: boolean;
    forget?: PlaybackChoiceKind[];
}

export interface StreamDecisionQuestion {
    serverId?: string;
    mediaPartId?: string;
    videoTrackId?: string;
    audioTrackId?: string;
    subtitleTrackId?: string;
}

// Object URLs for sidecar WebVTT, keyed by session + track. The endpoint needs
// the bearer header, so the VTT has to be fetched as a blob and handed to the
// <track> as an object URL — pointing <track src> at the endpoint would load
// without the header and 401.
const subtitleObjectUrls = new Map<string, string>();

const subtitleCacheKey = (sessionId: string, subtitleTrackId: string) => `${sessionId}|${subtitleTrackId}`;

export const streamingService = {
    startSession: async (mediaId: string, deviceId: string, startPosition: number = 0, videoTrackId?: string, audioTrackId?: string, subtitleTrackId?: string, serverId?: string, mediaPartId?: string) => {
        const capabilities = await scanDeviceCapabilities();

        const response = await apiClient.post('/streaming/start', {
            mediaId,
            deviceId,
            startPosition,
            videoTrackId,
            audioTrackId,
            subtitleTrackId,
            capabilities,
            mediaPartId
        }, { serverId });

        return response.data as StartSessionResponse;
    },
    previewDecision: async (mediaId: string, deviceId: string, question: StreamDecisionQuestion = {}) => {
        const capabilities = await scanDeviceCapabilities();
        const { serverId, ...tracks } = question;

        const response = await apiClient.post('/streaming/decision', {
            mediaId,
            deviceId,
            startPosition: 0,
            ...tracks,
            capabilities
        }, { serverId });

        return response.data as StreamDecisionPreview;
    },
    getRememberedChoice: async (mediaId: string, serverId?: string) => {
        const response = await apiClient.get(`/streaming/choices/${mediaId}`, { serverId });
        return response.data as RememberedChoice;
    },
    // mediaId is the item whose tracks were picked from (the episode on a
    // show's page); the choice is kept for its movie or show.
    rememberChoice: async (mediaId: string, change: RememberChoiceChange, serverId?: string) => {
        const response = await apiClient.put(`/streaming/choices/${mediaId}`, change, { serverId });
        return response.data as RememberedChoice;
    },
    startExtraSession: async (extraId: string, startPosition: number = 0, serverId?: string) => {
        const capabilities = await scanDeviceCapabilities();

        const response = await apiClient.post('/streaming/start-extra', {
            extraId,
            startPosition,
            capabilities
        }, { serverId });

        return response.data as StartSessionResponse;
    },
    pingSession: async (sessionId: string, currentPosition: number, duration: number, isPaused: boolean, serverId?: string) => {
        const safeDuration = Number.isFinite(duration) ? duration : 0;
        const safePosition = Number.isFinite(currentPosition) ? currentPosition : 0;

        await apiClient.put(`/streaming/sessions/${sessionId}/ping`, {
            currentPosition: safePosition,
            duration: safeDuration,
            isPaused
        }, { serverId });
    },
    stopSession: async (sessionId: string, serverId?: string) => {
        await apiClient.delete(`/streaming/sessions/${sessionId}`, { serverId });
    },
    getSessionStats: async (sessionId: string, serverId?: string): Promise<PlaybackStats> => {
        const response = await apiClient.get<PlaybackStats>(`/streaming/sessions/${sessionId}/stats`, { serverId });
        return response.data;
    },
    // Returns an object URL for the track's WebVTT, or null when the server has
    // nothing to give (404: not a text subtitle, or extraction failed). The URL
    // is cached per session + track so re-selecting is instant; call
    // releaseSubtitleUrls when the session ends.
    fetchSubtitleVtt: async (sessionId: string, subtitleTrackId: string, serverId?: string): Promise<string | null> => {
        const key = subtitleCacheKey(sessionId, subtitleTrackId);
        const cached = subtitleObjectUrls.get(key);
        if (cached) return cached;

        try {
            const response = await apiClient.get<Blob>(
                `/streaming/sessions/${sessionId}/subtitle/${subtitleTrackId}.vtt`,
                { serverId, responseType: 'blob' },
            );

            const objectUrl = URL.createObjectURL(response.data);
            subtitleObjectUrls.set(key, objectUrl);
            return objectUrl;
        } catch {
            return null;
        }
    },
    releaseSubtitleUrls: (sessionId: string) => {
        const prefix = `${sessionId}|`;
        for (const [key, url] of subtitleObjectUrls) {
            if (!key.startsWith(prefix)) continue;
            URL.revokeObjectURL(url);
            subtitleObjectUrls.delete(key);
        }
    }
};