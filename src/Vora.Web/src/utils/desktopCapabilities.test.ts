import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { desktopCapabilities, fakeDesktopBridge } from '../test/fakeDesktopBridge';
import type { DesktopSettings } from '../desktop/desktopBridge';

const DEVICE_CAPS_DTO_FIELDS = [
    'videoCodecs', 'audioCodecs', 'containers', 'maxAudioChannels', 'clientBandwidthKbps', 'requestedClientBitrateKbps',
    'requestedMaxResolution', 'supportedHdrFormats', 'maxVideoBitDepth', 'detectedAudioChannels', 'pickedAudioChannels',
    'playsAnyAudioLayout', 'canSwitchAudioTracks', 'hlsVideoCodecs', 'requestedMaxWidth', 'displayWidth', 'displayHeight',
    'measuredBandwidthKbps', 'rendersImageSubtitles',
];

async function freshModules() {
    vi.resetModules();
    const capabilities = await import('./desktopCapabilities');
    const scanner = await import('./hardwareScanner');
    return { ...capabilities, scanner };
}

function useBridge(settings?: Partial<DesktopSettings>, mpvAvailable = true) {
    const fake = fakeDesktopBridge(
        { mpv: { available: mpvAvailable, version: mpvAvailable ? '0.39.0' : null, reason: mpvAvailable ? null : 'Install mpv with sudo apt install mpv.' } },
        { useMpvForVideo: true, audioPassthrough: 'off', hdrOutput: true, hardwareDecoding: 'auto', ...settings },
    );
    window.voraDesktop = fake.bridge;
    return fake;
}

beforeEach(() => {
    localStorage.clear();
});

afterEach(() => {
    delete window.voraDesktop;
});

describe('the desktop capability profile', () => {
    it('sets every field the server reads from a client', async () => {
        const { desktopProfile } = await freshModules();

        const profile = desktopProfile(desktopCapabilities);

        for (const field of DEVICE_CAPS_DTO_FIELDS) {
            expect(profile, field).toHaveProperty(field);
        }
    });

    it('asks to play everything mpv can, with no tone mapping and no burned-in subtitles', async () => {
        const { desktopProfile } = await freshModules();

        const profile = desktopProfile(desktopCapabilities);

        expect(profile.videoCodecs).toEqual(desktopCapabilities.videoCodecs);
        expect(profile.containers).toContain('m2ts');
        expect(profile.supportedHdrFormats).toEqual(['HDR10', 'HLG', 'HDR10+', 'DolbyVision']);
        expect(profile.maxVideoBitDepth).toBe(12);
        expect(profile.rendersImageSubtitles).toBe(true);
        expect(profile.canSwitchAudioTracks).toBe(true);
        expect(profile.playsAnyAudioLayout).toBe(true);
        expect(profile.hlsVideoCodecs).toEqual(['h264', 'hevc', 'mpeg2video']);
        expect(profile.detectedAudioChannels).toBe(8);
        expect([profile.displayWidth, profile.displayHeight]).toEqual([3840, 2160]);
    });

    it('still lets the viewer cap the quality, as the browser does', async () => {
        const { desktopDeviceCapabilities } = await freshModules();
        localStorage.setItem('playback_prefs_profile_device', JSON.stringify({ bitrate: 20, maxResolution: 1080, maxAudioChannels: 6 }));
        localStorage.setItem('device_id', 'device');

        const capabilities = desktopDeviceCapabilities(desktopCapabilities, 'profile', 'device');

        expect(capabilities.requestedMaxResolution).toBe(1080);
        expect(capabilities.requestedMaxWidth).toBe(1920);
        expect(capabilities.pickedAudioChannels).toBe(6);
        expect(capabilities.maxAudioChannels).toBe(6);
    });
});

describe('the capabilities a play request sends', () => {
    it('are the browser scan when there is no desktop app', async () => {
        const { playbackCapabilities, scanner } = await freshModules();
        const scan = vi.spyOn(scanner, 'scanDeviceCapabilities');

        await playbackCapabilities();

        expect(scan).toHaveBeenCalled();
    });

    it('are the desktop profile when mpv plays the video', async () => {
        useBridge();
        const { playbackCapabilities } = await freshModules();

        const capabilities = await playbackCapabilities();

        expect(capabilities.rendersImageSubtitles).toBe(true);
        expect(capabilities.supportedHdrFormats).toContain('DolbyVision');
    });

    it('are the browser scan when the viewer turned the Vora player off', async () => {
        useBridge({ useMpvForVideo: false });
        const { playbackCapabilities } = await freshModules();

        expect((await playbackCapabilities()).rendersImageSubtitles).toBeUndefined();
    });

    it('are the browser scan when mpv is missing', async () => {
        useBridge({}, false);
        const { playbackCapabilities } = await freshModules();

        expect((await playbackCapabilities()).canSwitchAudioTracks).toBe(false);
    });
});
