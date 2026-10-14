import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useTitleTrackChoices } from './useTitleTrackChoices';
import { streamingService } from '../api/Streaming/streamingService';
import type { MediaItem, MediaPart } from '../api/Media/mediaService';
import type { DeviceCapabilities } from '../utils/hardwareScanner';
import { StorageKeys } from '../utils/storageKeys';

vi.mock('../api/Streaming/streamingService', () => ({
    streamingService: { previewDecision: vi.fn(), getRememberedChoice: vi.fn(), rememberChoice: vi.fn() },
}));

const preview = vi.mocked(streamingService.previewDecision);
const getRemembered = vi.mocked(streamingService.getRememberedChoice);
const remember = vi.mocked(streamingService.rememberChoice);

const caps: DeviceCapabilities = {
    videoCodecs: ['h264'], audioCodecs: ['aac', 'eac3'], containers: ['mkv'], maxAudioChannels: 6,
    detectedAudioChannels: 6, pickedAudioChannels: 0, playsAnyAudioLayout: true, canSwitchAudioTracks: false, hlsVideoCodecs: ['h264'],
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160, supportedHdrFormats: [], maxVideoBitDepth: 10,
};

const part: MediaPart = {
    id: 'part', resolution: '1080p', filePath: '/shows/episode.mkv',
    videoTracks: [{ id: 'video', codec: 'h264', isDefault: true }],
    audioTracks: [{ id: 'english', language: 'eng', codec: 'eac3', channels: 6, isDefault: true }, { id: 'japanese', language: 'jpn', codec: 'aac', channels: 2 }],
    subtitleTracks: [{ id: 'sdh', language: 'eng', codec: 'subrip', isHearingImpaired: true }],
};
const episode = { id: 'episode', title: 'Episode', type: 'Episode', mediaParts: [part] } as MediaItem;

const decision = (audioTrackId: string, subtitleTrackId: string | null) => ({
    mediaPartId: 'part', videoTrackId: 'video', audioTrackId, subtitleTrackId,
    strategy: 'DirectPlay', videoStrategy: 'DirectPlay', audioStrategy: 'DirectPlay', targetAudioChannels: 6,
});

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device');
    preview.mockReset();
    getRemembered.mockReset();
    remember.mockReset();
    getRemembered.mockResolvedValue({ remembersVersion: false, remembersAudio: false, subtitle: 'Automatic' });
});

describe('the tracks a title page shows and picks', () => {
    it('shows what the server will play, subtitle included', async () => {
        preview.mockResolvedValue(decision('english', 'sdh'));
        const { result } = renderHook(() => useTitleTrackChoices(episode, caps, 'server'));

        await waitFor(() => expect(result.current.subtitleId).toBe('sdh'));
        expect(result.current.audioId).toBe('english');
        expect(result.current.audioPickedHere).toBe(false);
        expect(getRemembered).toHaveBeenCalledWith('episode', 'server');
    });

    it('remembers a pick and then shows what the server plays with it', async () => {
        preview.mockResolvedValueOnce(decision('english', null)).mockResolvedValueOnce(decision('japanese', null));
        remember.mockResolvedValue({ remembersVersion: false, remembersAudio: true, subtitle: 'Automatic' });
        const { result } = renderHook(() => useTitleTrackChoices(episode, caps));
        await waitFor(() => expect(result.current.audioId).toBe('english'));

        await act(() => result.current.pickAudio('japanese'));

        expect(remember).toHaveBeenCalledWith('episode', { audioTrackId: 'japanese' }, undefined);
        expect(result.current.audioId).toBe('japanese');
        expect(result.current.remembered.remembersAudio).toBe(true);
        expect(result.current.audioPickedHere).toBe(false);
    });

    it('still uses a pick for this play when it cannot be saved', async () => {
        preview.mockResolvedValue(decision('english', null));
        remember.mockRejectedValue(new Error('offline'));
        const { result } = renderHook(() => useTitleTrackChoices(episode, caps));
        await waitFor(() => expect(result.current.audioId).toBe('english'));

        await act(() => result.current.pickAudio('japanese'));

        expect(result.current.audioId).toBe('japanese');
        expect(result.current.audioPickedHere).toBe(true);
    });

    it('turns subtitles off as a pick of its own and hands a kind back with Automatic', async () => {
        preview.mockResolvedValue(decision('english', null));
        remember.mockResolvedValue({ remembersVersion: false, remembersAudio: false, subtitle: 'Off' });
        const { result } = renderHook(() => useTitleTrackChoices(episode, caps));
        await waitFor(() => expect(result.current.audioId).toBe('english'));

        await act(() => result.current.pickSubtitle('none'));
        await act(() => result.current.chooseAutomatically('Subtitles'));

        expect(remember).toHaveBeenNthCalledWith(1, 'episode', { subtitlesOff: true }, undefined);
        expect(remember).toHaveBeenNthCalledWith(2, 'episode', { forget: ['Subtitles'] }, undefined);
    });
});
