import { beforeEach, describe, expect, it, vi } from 'vitest';
import { streamingService } from './streamingService';
import { apiClient } from '../client';

const capabilities = { detectedAudioChannels: 2, pickedAudioChannels: 6, playsAnyAudioLayout: true, canSwitchAudioTracks: false };

vi.mock('../client', () => ({
    apiClient: { post: vi.fn() },
}));

vi.mock('../../utils/hardwareScanner', () => ({
    scanDeviceCapabilities: vi.fn(async () => capabilities),
}));

const post = vi.mocked(apiClient.post);

beforeEach(() => {
    post.mockReset();
    post.mockResolvedValue({ data: { mediaPartId: 'part', videoTrackId: 'video', audioTrackId: 'audio' } });
});

describe('asking the server for its playback decision', () => {
    it('sends what this device can do and leaves the choice to the server', async () => {
        const decision = await streamingService.previewDecision('movie', 'device', { serverId: 'server' });

        expect(post).toHaveBeenCalledWith('/streaming/decision',
            { mediaId: 'movie', deviceId: 'device', startPosition: 0, capabilities },
            { serverId: 'server' });
        expect(decision.audioTrackId).toBe('audio');
    });

    it('names the version and subtitle the viewer is looking at', async () => {
        await streamingService.previewDecision('movie', 'device', { mediaPartId: 'part-4k', videoTrackId: 'video-4k', subtitleTrackId: 'pgs' });

        expect(post).toHaveBeenCalledWith('/streaming/decision',
            { mediaId: 'movie', deviceId: 'device', startPosition: 0, mediaPartId: 'part-4k', videoTrackId: 'video-4k', subtitleTrackId: 'pgs', capabilities },
            { serverId: undefined });
    });
});
