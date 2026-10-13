import { beforeEach, describe, expect, it, vi } from 'vitest';
import { streamingService } from './streamingService';
import { apiClient } from '../client';
import { mediaService, type MediaItem } from '../Media/mediaService';
import { scanDeviceCapabilities, scanDeviceCapabilitiesSync } from '../../utils/hardwareScanner';
import { saveTitlePlaybackPreferences } from '../../utils/titlePlaybackPreferences';
import { NoSubtitle } from '../../utils/subtitleKind';

vi.mock('../client', () => ({ apiClient: { post: vi.fn() } }));
vi.mock('../Media/mediaService', () => ({ mediaService: { getMediaItem: vi.fn(), getUpNext: vi.fn() } }));
vi.mock('../../utils/hardwareScanner', () => ({ scanDeviceCapabilities: vi.fn(), scanDeviceCapabilitiesSync: vi.fn() }));

const capabilities = { videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2, clientBandwidthKbps: 0, requestedClientBitrateKbps: 4000, requestedMaxResolution: 720, supportedHdrFormats: [], maxVideoBitDepth: 8 };
const episode: MediaItem = { id: 'episode', title: 'Episode', type: 'Episode', tvShowId: 'show', libraryId: 'library', collectionIds: [], mediaParts: [{ id: 'part', filePath: '', resolution: '1080p', videoTracks: [{ id: 'video', codec: 'h264' }], audioTracks: [{ id: 'audio', language: 'eng', codec: 'aac', channels: 6 }], subtitleTracks: [{ id: 'text', codec: 'srt', language: 'eng' }, { id: 'image', codec: 'pgs', language: 'fra' }] }] };
const show: MediaItem = { ...episode, id: 'show', type: 'TvShow', tvShowId: undefined, mediaParts: [] };

beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    vi.mocked(scanDeviceCapabilities).mockResolvedValue(capabilities);
    vi.mocked(scanDeviceCapabilitiesSync).mockReturnValue({ ...capabilities, requestedMaxResolution: 2160 });
    vi.mocked(mediaService.getMediaItem).mockResolvedValue(episode);
    vi.mocked(apiClient.post).mockResolvedValue({ data: { sessionId: 'session', audioTrackId: 'audio' } });
});

describe('saved title preferences at stream start', () => {
    it('leaves ordinary playback alone when no overrides are saved', async () => {
        await streamingService.startSession('episode', 'device');
        expect(mediaService.getMediaItem).not.toHaveBeenCalled();
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ capabilities }), { serverId: undefined });
    });
    it('applies show output and quality overrides to next-episode requests', async () => {
        saveTitlePlaybackPreferences(show, { maxAudioChannels: 6, maxResolution: 1080, bitrate: 0, audio: { id: 'old-id', language: 'en', codec: 'aac', channels: 6 } });
        await streamingService.startSession('episode', 'device');
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ mediaId: 'episode', audioTrackId: 'audio', mediaPartId: 'part', capabilities: expect.objectContaining({ maxAudioChannels: 6, requestedMaxResolution: 1080, requestedClientBitrateKbps: 0 }) }), { serverId: undefined });
    });
    it('sideloads saved text subtitles without requesting server burn-in', async () => {
        saveTitlePlaybackPreferences(show, { subtitles: { id: 'old-id', language: 'en' } });
        const session = await streamingService.startSession('episode', 'device');
        expect(session.textSubtitleTrackId).toBe('text');
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ subtitleTrackId: NoSubtitle }), { serverId: undefined });
    });
    it('requests saved image subtitles from the server', async () => {
        saveTitlePlaybackPreferences(show, { subtitles: { id: 'image', language: 'fra' } });
        const session = await streamingService.startSession('episode', 'device');
        expect(session.textSubtitleTrackId).toBeNull();
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ subtitleTrackId: 'image' }), { serverId: undefined });
    });
    it('sends explicit subtitle off even when default subtitles exist', async () => {
        saveTitlePlaybackPreferences(show, { subtitles: { id: 'none' } });
        const session = await streamingService.startSession('episode', 'device');
        expect(session.textSubtitleTrackId).toBeNull();
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ subtitleTrackId: NoSubtitle }), { serverId: undefined });
    });
    it('preserves explicit selections during stream changes', async () => {
        saveTitlePlaybackPreferences(show, { audio: { id: 'audio' }, subtitles: { id: 'text' }, maxAudioChannels: 6 });
        await streamingService.startSession('episode', 'device', 150, 'video', 'manual-audio', NoSubtitle, undefined, 'part');
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ videoTrackId: 'video', audioTrackId: 'manual-audio', subtitleTrackId: NoSubtitle, startPosition: 150 }), { serverId: undefined });
    });
    it('does not apply an unrelated movie override', async () => {
        saveTitlePlaybackPreferences({ ...episode, type: 'Movie', id: 'other-movie' }, { maxAudioChannels: 6 });
        await streamingService.startSession('episode', 'device');
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ capabilities }), { serverId: undefined });
    });
    it('reuses details supplied by the play screen', async () => {
        saveTitlePlaybackPreferences(show, { maxAudioChannels: 6 });
        await streamingService.startSession('episode', 'device', 0, undefined, undefined, undefined, undefined, undefined, episode);
        expect(mediaService.getMediaItem).not.toHaveBeenCalled();
    });
    it('resolves show selections against the actual next episode', async () => {
        saveTitlePlaybackPreferences(show, { audio: { id: 'old-id', language: 'en', channels: 6 } });
        vi.mocked(mediaService.getMediaItem).mockResolvedValueOnce(show).mockResolvedValueOnce(episode);
        vi.mocked(mediaService.getUpNext).mockResolvedValue({ nextItem: { id: 'episode', type: 'Episode', title: 'Episode' }, relatedLists: [] });
        await streamingService.startSession('show', 'device');
        expect(apiClient.post).toHaveBeenCalledWith('/streaming/start', expect.objectContaining({ mediaId: 'show', audioTrackId: 'audio' }), { serverId: undefined });
    });
});
