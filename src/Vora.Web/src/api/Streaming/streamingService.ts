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

export const streamingService = {
    startSession: async (mediaId: string, deviceId: string, startPosition: number = 0, videoTrackId?: string, audioTrackId?: string, subtitleTrackId?: string, serverId?: string) => {
        const capabilities = scanDeviceCapabilities();

        const response = await apiClient.post('/streaming/start', {
            mediaId,
            deviceId,
            startPosition,
            videoTrackId,
            audioTrackId,
            subtitleTrackId,
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
    }
};