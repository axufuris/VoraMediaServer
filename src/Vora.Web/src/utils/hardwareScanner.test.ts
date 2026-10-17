import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { applyUserCaps, detectAudioOutputChannels, detectHlsVideoCodecs, getEffectiveCapabilities, scanDeviceCapabilities, scanDeviceCapabilitiesSync, type DeviceCapabilities } from './hardwareScanner';
import { StorageKeys } from './storageKeys';

const close = vi.fn().mockResolvedValue(undefined);
let outputChannels = 2;

const device: DeviceCapabilities = {
    videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2,
    detectedAudioChannels: 2, pickedAudioChannels: 0, playsAnyAudioLayout: true, canSwitchAudioTracks: false, hlsVideoCodecs: ['h264'],
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160, requestedMaxWidth: 3840,
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

describe('video codecs the HLS player takes', () => {
    const mediaSourceTaking = (supported: (type: string) => boolean) =>
        vi.stubGlobal('MediaSource', { isTypeSupported: supported });

    it('reports HEVC when Media Source takes it, since hls.js repackages the segments for Media Source', async () => {
        mediaSourceTaking(() => true);
        expect(detectHlsVideoCodecs()).toEqual(['h264', 'hevc']);
        expect((await scanDeviceCapabilities()).hlsVideoCodecs).toEqual(['h264', 'hevc']);
    });

    it('reports only H.264 when Media Source cannot take HEVC', () => {
        mediaSourceTaking(type => !type.includes('hvc1'));
        expect(detectHlsVideoCodecs()).toEqual(['h264']);
    });

    it('reports only H.264 without Media Source, where the browser plays HLS itself', () => {
        vi.stubGlobal('MediaSource', undefined);
        expect(detectHlsVideoCodecs()).toEqual(['h264']);
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

    it('narrows the decoding width limit to the picked resolution as a widescreen box', () => {
        const result = applyUserCaps({ ...device, requestedMaxResolution: 1440, requestedMaxWidth: 3440 }, { maxResolution: 1080, maxAudioChannels: 0 });

        expect(result.requestedMaxResolution).toBe(1080);
        expect(result.requestedMaxWidth).toBe(1920);
    });

    it('keeps an explicit decoding limit when no resolution is picked', () => {
        const result = applyUserCaps({ ...device, requestedMaxResolution: 1440, requestedMaxWidth: 3440 }, { maxResolution: 0, maxAudioChannels: 0 });

        expect(result.requestedMaxResolution).toBe(1440);
        expect(result.requestedMaxWidth).toBe(3440);
    });

    it('applies the same saved pick to the picker and server capability report', async () => {
        localStorage.setItem(StorageKeys.deviceId, 'test-device');
        localStorage.setItem('playback_prefs_unknown_test-device', JSON.stringify({ maxAudioChannels: 6 }));
        expect(getEffectiveCapabilities('unknown', 'test-device').pickedAudioChannels).toBe(6);
        expect((await scanDeviceCapabilities()).pickedAudioChannels).toBe(6);
    });
});

describe('browser resolution preference', () => {
    it.each([[2560, 1440, 1], [1920, 1080, 1], [390, 844, 3]])('does not treat a %ix%i display at %ix scale as a decoding limit', (width, height, pixelRatio) => {
        vi.stubGlobal('screen', { width, height });
        vi.stubGlobal('devicePixelRatio', pixelRatio);

        const result = scanDeviceCapabilitiesSync();

        expect(result.requestedMaxResolution).toBe(0);
        expect(result.requestedMaxWidth).toBe(0);
    });

    it.each([[2560, 1440, 1, 2560, 1440], [1920, 1080, 2, 3840, 2160], [390, 844, 3, 2532, 1170]])(
        'reports a %ix%i display at %ix scale as a %ix%i screen hint, landscape',
        (width, height, pixelRatio, hintWidth, hintHeight) => {
            vi.stubGlobal('screen', { width, height });
            vi.stubGlobal('devicePixelRatio', pixelRatio);

            const result = scanDeviceCapabilitiesSync();

            expect(result.displayWidth).toBe(hintWidth);
            expect(result.displayHeight).toBe(hintHeight);
            expect(applyUserCaps(result, { maxResolution: 0, maxAudioChannels: 0 }).displayHeight).toBe(hintHeight);
        });

    it.each([720, 1080, 2160])('applies an explicit %ip limit when the decoder limit is unknown', maxResolution => {
        const result = applyUserCaps(scanDeviceCapabilitiesSync(), { maxResolution, maxAudioChannels: 0 });

        expect(result.requestedMaxResolution).toBe(maxResolution);
        expect(result.requestedMaxWidth).toBe(Math.round(maxResolution * 16 / 9));
    });

    it('does not raise a known decoding limit to match a higher preference', () => {
        const result = applyUserCaps({ ...device, requestedMaxResolution: 1080, requestedMaxWidth: 1920 }, { maxResolution: 2160, maxAudioChannels: 0 });

        expect(result.requestedMaxResolution).toBe(1080);
        expect(result.requestedMaxWidth).toBe(1920);
    });

    it.each([0, 1080, 2160])('reports the same saved resolution (%i) for the picker and stream start', async maxResolution => {
        vi.stubGlobal('screen', { width: 2560, height: 1440 });
        localStorage.setItem(StorageKeys.deviceId, 'test-device');
        localStorage.setItem('playback_prefs_unknown_test-device', JSON.stringify({ maxResolution }));

        const picker = getEffectiveCapabilities('unknown', 'test-device');
        const stream = await scanDeviceCapabilities();

        expect(picker.requestedMaxResolution).toBe(maxResolution);
        expect(stream.requestedMaxResolution).toBe(maxResolution);
        expect(stream.requestedMaxWidth).toBe(picker.requestedMaxWidth);
    });

    it('keeps the SDR display restriction even when HEVC Main10 decoding is supported', async () => {
        vi.stubGlobal('MediaSource', { isTypeSupported: () => true });

        const result = await scanDeviceCapabilities();

        expect(result.videoCodecs).toContain('hevc');
        expect(result.maxVideoBitDepth).toBe(10);
        expect(result.supportedHdrFormats).toEqual([]);
    });

    it('keeps HDR and HEVC HLS support on a smaller HDR display in Original mode', async () => {
        vi.stubGlobal('screen', { width: 2560, height: 1440 });
        vi.stubGlobal('MediaSource', { isTypeSupported: () => true });
        vi.stubGlobal('matchMedia', () => ({ matches: true }));

        const result = await scanDeviceCapabilities();

        expect(result.requestedMaxResolution).toBe(0);
        expect(result.requestedMaxWidth).toBe(0);
        expect(result.supportedHdrFormats).toContain('HDR10');
        expect(result.hlsVideoCodecs).toContain('hevc');
    });
});
