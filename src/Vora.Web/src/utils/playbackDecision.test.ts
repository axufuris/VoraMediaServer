import { describe, expect, it } from 'vitest';
import { isVideoDirectPlayable, renderableHdrFormats } from './playbackDecision';
import type { DeviceCapabilities } from './hardwareScanner';

const caps = (supportedHdrFormats: string[]): DeviceCapabilities => ({
    videoCodecs: ['hevc', 'h264'],
    audioCodecs: ['aac'],
    containers: ['mkv', 'mp4'],
    maxAudioChannels: 2,
    detectedAudioChannels: 2,
    pickedAudioChannels: 0,
    playsAnyAudioLayout: true,
    canSwitchAudioTracks: false,
    hlsVideoCodecs: ['h264'],
    clientBandwidthKbps: 0,
    requestedClientBitrateKbps: 0,
    requestedMaxResolution: 0,
    requestedMaxWidth: 0,
    supportedHdrFormats,
    maxVideoBitDepth: 0,
});

const part = { resolution: '2160p' };

describe('renderableHdrFormats', () => {
    it.each([
        ['DoVi', ['DOLBYVISION']],
        ['DoVi/HDR10', ['DOLBYVISION', 'HDR10']],
        ['DoVi/HDR10Plus', ['DOLBYVISION', 'HDR10PLUS', 'HDR10']],
        ['DoVi/HLG', ['DOLBYVISION', 'HLG']],
        ['HDR10Plus', ['HDR10PLUS', 'HDR10']],
        ['HDR10', ['HDR10']],
        ['HLG', ['HLG']],
        ['SDR', []],
        [undefined, []],
    ])('lets %s show on %j', (hdrType, formats) => {
        expect(renderableHdrFormats(hdrType)).toEqual(formats);
    });
});

describe('isVideoDirectPlayable', () => {
    it('plays a profile 8 Dolby Vision file on an HDR10 screen through its HDR10 picture', () => {
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'DoVi/HDR10' }, part, caps(['HDR10']))).toBe(true);
    });

    it('plays an HLG-based Dolby Vision file on an HLG screen', () => {
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'DoVi/HLG' }, part, caps(['HLG']))).toBe(true);
    });

    it('needs Dolby Vision for a file that has no other picture', () => {
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'DoVi' }, part, caps(['HDR10']))).toBe(false);
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'DoVi' }, part, caps(['DolbyVision']))).toBe(true);
    });

    it('needs some HDR on the screen for an HDR file', () => {
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'HDR10' }, part, caps([]))).toBe(false);
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'DoVi/HDR10' }, part, caps([]))).toBe(false);
    });

    it('checks a measured picture against the width of the screen as well as its height', () => {
        const screen = { ...caps([]), requestedMaxResolution: 1440, requestedMaxWidth: 2560 };

        expect(isVideoDirectPlayable({ codec: 'hevc', width: 3840, height: 1540 }, part, screen)).toBe(false);
        expect(isVideoDirectPlayable({ codec: 'hevc', width: 2560, height: 1072 }, part, screen)).toBe(true);
    });

    it('goes by the file-name label only for a track that was never measured', () => {
        const screen = { ...caps([]), requestedMaxResolution: 1080, requestedMaxWidth: 1920 };

        expect(isVideoDirectPlayable({ codec: 'hevc' }, { resolution: '2160p' }, screen)).toBe(false);
        expect(isVideoDirectPlayable({ codec: 'hevc', width: 1920, height: 800 }, { resolution: '2160p' }, screen)).toBe(true);
    });

    it('plays standard dynamic range anywhere', () => {
        expect(isVideoDirectPlayable({ codec: 'hevc' }, part, caps([]))).toBe(true);
        expect(isVideoDirectPlayable({ codec: 'hevc', hdrType: 'SDR' }, part, caps([]))).toBe(true);
    });
});
