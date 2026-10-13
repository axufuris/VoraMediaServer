import { describe, expect, it } from 'vitest';
import type { DeviceCapabilities } from './hardwareScanner';
import { pickBestAudioTrack } from './audioTrackSelection';

const capabilities = (channels: number, codecs = ['aac', 'eac3']): DeviceCapabilities => ({
    videoCodecs: ['h264'], audioCodecs: codecs, containers: ['mp4'], maxAudioChannels: channels,
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 1080,
    supportedHdrFormats: [], maxVideoBitDepth: 8,
});

const tracks = [
    { id: 'surround', codec: 'eac3', channels: 6, isDefault: true, language: 'eng' },
    { id: 'stereo', codec: 'aac', channels: 2, language: 'en' },
    { id: '7.1', codec: 'aac', channels: 8, language: 'eng' },
];

describe('automatic audio selection', () => {
    it.each([[2, 'stereo'], [6, 'surround'], [8, '7.1']] as const)('matches a %i channel output', (channels, id) => {
        expect(pickBestAudioTrack(tracks, capabilities(channels))?.id).toBe(id);
    });

    it('uses compatible stereo when the surround codec cannot play', () => {
        expect(pickBestAudioTrack(tracks.slice(0, 2), capabilities(6, ['aac']))?.id).toBe('stereo');
    });

    it('avoids downmixing when neither original codec can play', () => {
        expect(pickBestAudioTrack(tracks.slice(0, 2), capabilities(2, []))?.id).toBe('stereo');
    });

    it('prefers surround over a default stereo track', () => {
        const choices = tracks.slice(0, 2).map(track => ({ ...track, isDefault: track.id === 'stereo' }));
        expect(pickBestAudioTrack(choices, capabilities(6))?.id).toBe('surround');
    });

    it('prefers a higher quality compatible codec within the same layout', () => {
        const choices = [{ id: 'aac', codec: 'aac', channels: 6, isDefault: true }, { id: 'eac3', codec: 'eac3', channels: 6 }];
        expect(pickBestAudioTrack(choices, capabilities(6))?.id).toBe('eac3');
    });

    it('preserves dialogue language while switching channel layouts', () => {
        const choices = [...tracks.slice(0, 2), { id: 'french', codec: 'aac', channels: 6, language: 'fra' }];
        expect(pickBestAudioTrack(choices, capabilities(6, ['aac']))?.id).toBe('stereo');
    });

    it('does not automatically switch to commentary to avoid conversion', () => {
        const choices = [tracks[0], { ...tracks[1], title: 'Director commentary' }];
        expect(pickBestAudioTrack(choices, capabilities(2))?.id).toBe('surround');
    });

    it('allows commentary when it is the only track', () => {
        expect(pickBestAudioTrack([{ ...tracks[1], title: 'Commentary' }], capabilities(2))?.id).toBe('stereo');
    });

    it('handles titles with no audio tracks', () => {
        expect(pickBestAudioTrack([], capabilities(2))).toBeUndefined();
        expect(pickBestAudioTrack(undefined, capabilities(2))).toBeUndefined();
    });
});
