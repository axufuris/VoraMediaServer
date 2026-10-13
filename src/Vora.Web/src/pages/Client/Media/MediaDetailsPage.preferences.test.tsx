import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import MediaDetailsPage from './MediaDetailsPage';
import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import { streamingService } from '../../../api/Streaming/streamingService';
import { readTitlePlaybackPreferences, saveTitlePlaybackPreferences } from '../../../utils/titlePlaybackPreferences';
import { StorageKeys } from '../../../utils/storageKeys';

const { playMedia, alert } = vi.hoisted(() => ({ playMedia: vi.fn(), alert: vi.fn() }));
vi.mock('../../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia, isPlaying: true }) }));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ alert }) }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: vi.fn() }));
vi.mock('../../../api/Media/mediaService', () => ({ mediaService: { getMediaItem: vi.fn(), getUpNext: vi.fn() } }));
vi.mock('../../../api/Watchlist/watchlistService', () => ({ watchlistService: { check: vi.fn().mockResolvedValue(false) } }));
vi.mock('../../../api/Streaming/streamingService', () => ({ streamingService: { startSession: vi.fn() } }));
vi.mock('../../../components/Media/ItemMenu/useMediaItemActions', () => ({ useMediaItemActions: () => ({ entries: [], dialogs: null }) }));
vi.mock('../../../utils/hardwareScanner', () => {
    const caps = { videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2, requestedClientBitrateKbps: 0, requestedMaxResolution: 2160, supportedHdrFormats: [], maxVideoBitDepth: 8, clientBandwidthKbps: 0 };
    return { getEffectiveCapabilities: () => caps, scanDeviceCapabilitiesSync: () => caps };
});

const movie: MediaItem = { id: 'movie', title: 'A Movie', type: 'Movie', libraryId: 'library', collectionIds: [], mediaParts: [{ id: 'part', filePath: '', resolution: '1080p', videoTracks: [{ id: 'video', codec: 'h264' }], audioTracks: [{ id: 'stereo', codec: 'aac', channels: 2, language: 'eng' }, { id: 'surround', codec: 'aac', channels: 6, language: 'eng' }], subtitleTracks: [{ id: 'text', language: 'eng', codec: 'srt', isDefault: true }] }] };
const episode: MediaItem = { ...movie, id: 'episode', title: 'Episode One', type: 'Episode', tvShowId: 'show', tvShowTitle: 'A Show', seasonNumber: 1, episodeNumber: 1, resumePositionSeconds: 120 };
const show: MediaItem = { ...movie, id: 'show', title: 'A Show', type: 'TvShow', mediaParts: [] };
const mount = (id = 'movie') => render(<MemoryRouter initialEntries={[`/media/${id}`]}><Routes><Route path="/media/:id" element={<MediaDetailsPage />} /></Routes></MemoryRouter>);

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device');
    vi.clearAllMocks();
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
    vi.mocked(mediaService.getMediaItem).mockResolvedValue(movie);
    vi.mocked(streamingService.startSession).mockResolvedValue({ sessionId: 'session', streamUrl: '/stream', videoTrackId: 'video', audioTrackId: 'surround', subtitleTrackId: null, strategy: 'DirectPlay', videoStrategy: 'DirectPlay', audioStrategy: 'DirectPlay', subtitleStrategy: 'None', videoCodec: 'h264', audioCodec: 'aac', targetAudioChannels: 6, container: 'mp4', bandwidthKbps: 0 });
});

describe('movie and show playback preferences', () => {
    it('restores saved audio and subtitle off after leaving and reopening the movie', async () => {
        const page = mount();
        await screen.findByRole('combobox', { name: 'Audio', exact: true });
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio', exact: true }), 'surround');
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Subtitles' }), 'none');
        page.unmount();
        mount();
        expect(await screen.findByRole('combobox', { name: 'Audio', exact: true })).toHaveValue('surround');
        expect(screen.getByRole('combobox', { name: 'Subtitles' })).toHaveValue('none');
    });
    it('sends manual choices when starting and shows the returned active audio', async () => {
        mount();
        await screen.findByRole('combobox', { name: 'Audio', exact: true });
        await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Audio', exact: true }), 'surround');
        await userEvent.click(screen.getByRole('button', { name: 'Play', exact: true }));
        expect(streamingService.startSession).toHaveBeenCalledWith('movie', 'device', 0, 'video', 'surround', '00000000-0000-0000-0000-000000000000', undefined, 'part', movie);
        expect(playMedia).toHaveBeenCalledWith(expect.objectContaining({ audioChannels: 6, textSubtitleTrackId: 'text' }));
    });
    it('reset removes saved track and capability overrides', async () => {
        saveTitlePlaybackPreferences(movie, { maxAudioChannels: 6, subtitles: { id: 'none' } });
        mount();
        await screen.findByRole('combobox', { name: 'Audio output' });
        await userEvent.click(screen.getByRole('button', { name: 'Use global defaults' }));
        expect(readTitlePlaybackPreferences(movie).maxAudioChannels).toBeUndefined();
        expect(screen.getByRole('combobox', { name: 'Audio output' })).toHaveValue('global');
        expect(screen.getByRole('combobox', { name: 'Subtitles' })).toHaveValue('text');
    });
    it('starts the actual next episode with inherited show choices and its resume position', async () => {
        vi.mocked(mediaService.getMediaItem).mockImplementation(async id => id === 'show' ? show : episode);
        vi.mocked(mediaService.getUpNext).mockResolvedValue({ nextItem: { id: 'episode', title: 'Episode One', type: 'Episode' }, relatedLists: [] });
        saveTitlePlaybackPreferences(show, { audio: { id: 'old-audio', language: 'en', channels: 6, codec: 'aac' }, subtitles: { id: 'none' } });
        mount('show');
        await waitFor(() => expect(screen.getByRole('combobox', { name: 'Audio', exact: true })).toHaveValue('surround'));
        await userEvent.click(screen.getByRole('button', { name: 'Play next', exact: true }));
        expect(streamingService.startSession).toHaveBeenCalledWith('episode', 'device', 120, 'video', 'surround', '00000000-0000-0000-0000-000000000000', undefined, 'part', episode);
        expect(playMedia).toHaveBeenCalledWith(expect.objectContaining({ id: 'episode', playbackContextType: 'tvshow' }));
    });
});
