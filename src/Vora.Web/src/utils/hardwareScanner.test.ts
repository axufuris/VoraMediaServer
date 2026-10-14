import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { applyUserCaps, detectAudioOutputChannels, getEffectiveCapabilities, scanDeviceCapabilities, type DeviceCapabilities } from './hardwareScanner';
import { StorageKeys } from './storageKeys';

const close = vi.fn().mockResolvedValue(undefined);
let outputChannels = 2;

const device: DeviceCapabilities = {
    videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2,
    detectedAudioChannels: 2, pickedAudioChannels: 0, playsAnyAudioLayout: true, canSwitchAudioTracks: false,
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160,
    supportedHdrFormats: ['HDR10'], maxVideoBitDepth: 10,
};

beforeEach(() => {
    localStorage.clear();
    close.mockClear();
    outputChannels = 2;
    vi.stubGlobal('AudioContext', class {
        destination = { maxChannelCount: outputChannels };
        close = close;
    });
    vi.spyOn(HTMLMediaElement.prototype, 'canPlayType').mockReturnValue('probably');
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
});

afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
});

describe('audio output detection', () => {
    it.each([2, 6, 8])('reports the browser output as %i channels and closes the context', channels => {
        outputChannels = channels;
        expect(detectAudioOutputChannels()).toBe(channels);
        expect(close).toHaveBeenCalledOnce();
    });

    it.each([0, -1, Number.NaN, Number.POSITIVE_INFINITY])('falls back to stereo for an invalid result (%s)', channels => {
        outputChannels = channels;
        expect(detectAudioOutputChannels()).toBe(2);
    });

    it('bounds large device reports to the supported 7.1 layout', () => {
        outputChannels = 32;
        expect(detectAudioOutputChannels()).toBe(8);
    });

    it('falls back to stereo when the browser cannot probe the output', () => {
        vi.stubGlobal('AudioContext', class { constructor() { throw new Error('unavailable'); } });
        expect(detectAudioOutputChannels()).toBe(2);
    });

    it('rescans the output on each playback request after a system change', async () => {
        expect((await scanDeviceCapabilities()).detectedAudioChannels).toBe(2);
        outputChannels = 6;
        expect((await scanDeviceCapabilities()).detectedAudioChannels).toBe(6);
    });

    it('tells the server a browser mixes any layout down itself and cannot switch tracks', async () => {
        const capabilities = await scanDeviceCapabilities();
        expect(capabilities.playsAnyAudioLayout).toBe(true);
        expect(capabilities.canSwitchAudioTracks).toBe(false);
    });
});

describe('per-device audio output preference', () => {
    it.each([2, 6, 8])('reports a %i channel pick beside the stereo the browser detected', channels => {
        const result = applyUserCaps(device, { maxResolution: 0, maxAudioChannels: channels });
        expect(result.pickedAudioChannels).toBe(channels);
        expect(result.detectedAudioChannels).toBe(2);
    });

    it('reports no pick in automatic mode', () => {
        const result = applyUserCaps({ ...device, detectedAudioChannels: 6 }, { maxResolution: 0, maxAudioChannels: 0 });
        expect(result.pickedAudioChannels).toBe(0);
        expect(result.detectedAudioChannels).toBe(6);
    });

    it.each([-1, 9, Number.NaN, 2.5])('ignores an invalid pick (%s)', channels => {
        expect(applyUserCaps(device, { maxResolution: 0, maxAudioChannels: channels }).pickedAudioChannels).toBe(0);
    });

    it.each([[2, 6, 2], [6, 2, 2], [6, 6, 6], [0, 6, 6]])('keeps the single older value at the lower of a %i pick and %i detected', (picked, detected, expected) => {
        const result = applyUserCaps({ ...device, detectedAudioChannels: detected }, { maxResolution: 0, maxAudioChannels: picked });
        expect(result.maxAudioChannels).toBe(expected);
    });

    it('preserves codec, HDR and resolution limits when overriding output channels', () => {
        const result = applyUserCaps(device, { maxResolution: 1080, maxAudioChannels: 6 });
        expect(result.audioCodecs).toEqual(['aac']);
        expect(result.supportedHdrFormats).toEqual(['HDR10']);
        expect(result.requestedMaxResolution).toBe(1080);
    });

    it('applies the same saved pick to the picker and server capability report', async () => {
        localStorage.setItem(StorageKeys.deviceId, 'test-device');
        localStorage.setItem('playback_prefs_unknown_test-device', JSON.stringify({ maxAudioChannels: 6 }));
        expect(getEffectiveCapabilities('unknown', 'test-device').pickedAudioChannels).toBe(6);
        expect((await scanDeviceCapabilities()).pickedAudioChannels).toBe(6);
    });
});
