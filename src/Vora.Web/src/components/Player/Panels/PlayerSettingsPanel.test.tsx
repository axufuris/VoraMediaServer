import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import PlayerSettingsPanel from './PlayerSettingsPanel';
import type { MediaItem } from '../../../api/Media/mediaService';
import type { DeviceCapabilities } from '../../../utils/hardwareScanner';

const caps: DeviceCapabilities = {
    videoCodecs: ['h264'], audioCodecs: ['eac3', 'ac3', 'aac'], containers: ['mkv', 'mp4'], maxAudioChannels: 8,
    detectedAudioChannels: 8, pickedAudioChannels: 0, playsAnyAudioLayout: true, canSwitchAudioTracks: false, hlsVideoCodecs: ['h264'],
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160, requestedMaxWidth: 3840,
    displayWidth: 3840, displayHeight: 2160, measuredBandwidthKbps: 0, supportedHdrFormats: [], maxVideoBitDepth: 10,
};

const media = (channels: Array<number | undefined>) => ({
    id: 'm1',
    title: 'Film',
    mediaParts: [{
        id: 'p1',
        videoTracks: [{ id: 'v1', codec: 'h264', width: 1920, height: 1080 }],
        audioTracks: channels.map((count, index) => ({ id: `a${index}`, codec: 'eac3', language: 'eng', channels: count, isDefault: index === 0 })),
        subtitleTracks: [],
    }],
}) as unknown as MediaItem;

const renderPanel = (channels: Array<number | undefined>) => render(
    <PlayerSettingsPanel
        mediaDetails={media(channels)}
        selVideo="v1"
        selAudio="a0"
        setSelVideo={vi.fn()}
        setSelAudio={vi.fn()}
        caps={caps}
        onVideoPartChanged={vi.fn()}
        onCancel={vi.fn()}
        onApply={vi.fn()}
    />,
);

describe('PlayerSettingsPanel audio list', () => {
    it('shows 7.1 and 5.1 for 8 and 6 channels, not 8ch and 6ch', () => {
        renderPanel([8, 6]);

        const options = screen.getAllByRole('option').map(option => option.textContent ?? '');
        const audio = options.filter(text => text.includes('EAC3'));
        expect(audio).toHaveLength(2);
        expect(audio[0]).toContain('EAC3 (7.1)');
        expect(audio[1]).toContain('EAC3 (5.1)');
        expect(audio.join(' ')).not.toMatch(/\dch/);
    });

    it('shows stereo as 2.0 and leaves out the brackets when the file has no channel count', () => {
        renderPanel([2, undefined]);

        const audio = screen.getAllByRole('option').map(option => option.textContent ?? '').filter(text => text.includes('EAC3'));
        expect(audio[0]).toContain('EAC3 (2.0)');
        expect(audio[1]).toContain('EAC3 ·');
        expect(audio[1]).not.toContain('(');
    });
});
