import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import TrackChoiceChips from './TrackChoiceChips';
import type { MediaItem, MediaPart } from '../../api/Media/mediaService';
import type { RememberedChoice } from '../../api/Streaming/streamingService';
import type { DeviceCapabilities } from '../../utils/hardwareScanner';

const caps: DeviceCapabilities = {
    videoCodecs: ['h264', 'hevc'], audioCodecs: ['aac', 'ac3'], containers: ['mkv', 'mp4'], maxAudioChannels: 6,
    detectedAudioChannels: 6, pickedAudioChannels: 0, playsAnyAudioLayout: true, canSwitchAudioTracks: false, hlsVideoCodecs: ['h264'],
    clientBandwidthKbps: 100000, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160, requestedMaxWidth: 3840, supportedHdrFormats: [], maxVideoBitDepth: 10,
};

const hd: MediaPart = {
    id: 'hd', resolution: '1080p', filePath: '/movies/film.mkv',
    videoTracks: [{ id: 'hd-video', codec: 'h264', isDefault: true }],
    audioTracks: [{ id: 'english', language: 'eng', codec: 'ac3', channels: 6, isDefault: true }, { id: 'japanese', language: 'jpn', codec: 'aac', channels: 2 }],
    subtitleTracks: [{ id: 'sdh', language: 'eng', codec: 'subrip', isHearingImpaired: true }],
};
const uhd: MediaPart = {
    id: 'uhd', resolution: '2160p', filePath: '/movies/film-4k.mkv',
    videoTracks: [{ id: 'uhd-video', codec: 'hevc', isDefault: true }],
    audioTracks: hd.audioTracks,
};

const film = (parts: MediaPart[]) => ({ id: 'film', title: 'Film', type: 'Movie', mediaParts: parts } as MediaItem);
const automatic: RememberedChoice = { remembersVersion: false, remembersAudio: false, subtitle: 'Automatic' };

const handlers = () => ({ onPickVersion: vi.fn(), onPickAudio: vi.fn(), onPickSubtitle: vi.fn(), onChooseAutomatically: vi.fn() });

beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
});

afterEach(() => {
    vi.unstubAllGlobals();
});

describe('the track chips on a title page', () => {
    it('opens the audio chip as a menu with Automatic in use when nothing was picked', () => {
        const on = handlers();
        render(<TrackChoiceChips item={film([hd])} activePart={hd} videoId="hd-video" audioId="english" subtitleId="none"
            remembered={automatic} pickReplaced={false} caps={caps} {...on} />);

        fireEvent.click(screen.getByRole('button', { name: 'Audio' }));

        expect(screen.getByRole('menuitemradio', { name: /^Automatic/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('menuitemradio', { name: /^English · AC3 5\.1/ })).toHaveAttribute('aria-checked', 'false');
        fireEvent.click(screen.getByRole('menuitemradio', { name: /^Japanese · AAC 2\.0/ }));
        expect(on.onPickAudio).toHaveBeenCalledWith('japanese');
    });

    it('marks the remembered pick and hands the choice back with Automatic', () => {
        const on = handlers();
        render(<TrackChoiceChips item={film([hd])} activePart={hd} videoId="hd-video" audioId="japanese" subtitleId="sdh"
            remembered={{ remembersVersion: false, remembersAudio: true, subtitle: 'Language' }} pickReplaced={false} caps={caps} {...on} />);

        fireEvent.click(screen.getByRole('button', { name: 'Subtitles' }));
        expect(screen.getByRole('menuitemradio', { name: /^English SDH/ })).toHaveAttribute('aria-checked', 'true');
        fireEvent.click(screen.getByRole('menuitemradio', { name: /^Automatic/ }));

        expect(on.onChooseAutomatically).toHaveBeenCalledWith('Subtitles');
    });

    it('keeps a chip with nothing else to choose as a plain chip', () => {
        render(<TrackChoiceChips item={film([hd])} activePart={hd} videoId="hd-video" audioId="english" subtitleId="none"
            remembered={automatic} pickReplaced={false} caps={caps} {...handlers()} />);

        expect(screen.queryByRole('button', { name: 'Version' })).toBeNull();
        expect(screen.getByText('1080p')).toBeInTheDocument();
    });

    it('offers every version and says when the pick was swapped for one that plays', () => {
        const on = handlers();
        render(<TrackChoiceChips item={film([hd, uhd])} activePart={hd} videoId="hd-video" audioId="english" subtitleId="none"
            remembered={{ remembersVersion: true, remembersAudio: false, subtitle: 'Automatic' }} pickReplaced caps={caps} {...on} />);

        expect(screen.getByText(/closest one that plays/)).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Version' }));
        fireEvent.click(screen.getByRole('menuitemradio', { name: /^4K · HEVC/ }));

        expect(on.onPickVersion).toHaveBeenCalledWith('uhd');
    });
});
