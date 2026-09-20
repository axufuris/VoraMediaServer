import { apiClient } from '../client';
import { scanDeviceCapabilities } from '../../utils/hardwareScanner';

export interface StartSessionResponse {
    sessionId: string;
    streamUrl: string;
    videoTrackId: string;
    audioTrackId: string;
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