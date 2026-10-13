import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { applyUserCaps, detectAudioOutputChannels, getEffectiveCapabilities, scanDeviceCapabilities, type DeviceCapabilities } from './hardwareScanner';
import { StorageKeys } from './storageKeys';

const close = vi.fn().mockResolvedValue(undefined);
let outputChannels = 2;

const device: DeviceCapabilities = {
    videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2,
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
        expect((await scanDeviceCapabilities()).maxAudioChannels).toBe(2);
        outputChannels = 6;
        expect((await scanDeviceCapabilities()).maxAudioChannels).toBe(6);
    });
});

describe('per-device audio output preference', () => {
    it.each([2, 6, 8])('uses the explicit %i channel layout even when the browser reports stereo', channels => {
        expect(applyUserCaps(device, { maxResolution: 0, maxAudioChannels: channels }).maxAudioChannels).toBe(channels);
    });

    it('uses the reported layout in automatic mode', () => {
        expect(applyUserCaps({ ...device, maxAudioChannels: 6 }, { maxResolution: 0, maxAudioChannels: 0 }).maxAudioChannels).toBe(6);
    });

    it.each([-1, 9, Number.NaN, 2.5])('ignores an invalid override (%s)', channels => {
        expect(applyUserCaps(device, { maxResolution: 0, maxAudioChannels: channels }).maxAudioChannels).toBe(2);
    });

    it('preserves codec, HDR and resolution limits when overriding output channels', () => {
        const result = applyUserCaps(device, { maxResolution: 1080, maxAudioChannels: 6 });
        expect(result.audioCodecs).toEqual(['aac']);
        expect(result.supportedHdrFormats).toEqual(['HDR10']);
        expect(result.requestedMaxResolution).toBe(1080);
    });

    it('applies the same saved output override to the picker and server capability report', async () => {
        localStorage.setItem(StorageKeys.deviceId, 'test-device');
        localStorage.setItem('playback_prefs_unknown_test-device', JSON.stringify({ maxAudioChannels: 6 }));
        expect(getEffectiveCapabilities('unknown', 'test-device').maxAudioChannels).toBe(6);
        expect((await scanDeviceCapabilities()).maxAudioChannels).toBe(6);
    });
});
