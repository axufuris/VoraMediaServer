import { beforeEach, describe, expect, it } from 'vitest';
import type { MediaItem, MediaPart } from '../api/Media/mediaService';
import type { DeviceCapabilities } from './hardwareScanner';
import { serverVault } from './serverVault';
import { StorageKeys } from './storageKeys';
import { applyTitlePlaybackCapabilities, hasTitlePlaybackPreferences, readTitlePlaybackPreferences, resolveTitlePlayback, saveAudioChoice, saveSubtitleChoice, saveTitlePlaybackPreferences, saveVideoChoice, titlePlaybackKey } from './titlePlaybackPreferences';

const caps: DeviceCapabilities = { videoCodecs: ['h264'], audioCodecs: ['aac'], containers: ['mp4'], maxAudioChannels: 2, clientBandwidthKbps: 0, requestedClientBitrateKbps: 4000, requestedMaxResolution: 720, supportedHdrFormats: [], maxVideoBitDepth: 8 };
const part = { id: 'part-1', filePath: '', resolution: '1080p', videoTracks: [{ id: 'video-1', codec: 'h264', isDefault: true }], audioTracks: [{ id: 'surround', codec: 'aac', channels: 6, language: 'eng' }, { id: 'stereo', codec: 'aac', channels: 2, language: 'eng' }], subtitleTracks: [{ id: 'sub-1', language: 'eng', codec: 'srt', isDefault: true }, { id: 'sub-2', language: 'fra', codec: 'srt' }] } satisfies MediaPart;
const movie: MediaItem = { id: 'movie-1', title: 'Movie', type: 'Movie', libraryId: 'library', collectionIds: [], mediaParts: [part] };
const episode: MediaItem = { ...movie, id: 'episode-1', type: 'Episode', tvShowId: 'show-1' };

beforeEach(() => localStorage.clear());

describe('title playback persistence', () => {
    it('restores tracks, explicit subtitle off and output overrides after reading storage again', () => {
        const preferences = { video: saveVideoChoice(part, part.videoTracks[0]), audio: saveAudioChoice(part.audioTracks[0]), subtitles: saveSubtitleChoice(), maxResolution: 1080, bitrate: 0, maxAudioChannels: 6 };
        saveTitlePlaybackPreferences(movie, preferences);
        expect(readTitlePlaybackPreferences(movie)).toEqual(preferences);
        expect(hasTitlePlaybackPreferences()).toBe(true);
    });
    it('shares a show choice with every episode', () => {
        const show = { ...movie, id: 'show-1', type: 'TvShow' };
        saveTitlePlaybackPreferences(show, { subtitles: saveSubtitleChoice() });
        expect(readTitlePlaybackPreferences(episode).subtitles?.id).toBe('none');
        expect(readTitlePlaybackPreferences({ ...episode, id: 'episode-2' }).subtitles?.id).toBe('none');
    });
    it('keeps different movies independent', () => {
        saveTitlePlaybackPreferences(movie, { maxAudioChannels: 6 });
        expect(readTitlePlaybackPreferences({ ...movie, id: 'movie-2' }).maxAudioChannels).toBeUndefined();
    });
    it('keeps devices independent', () => {
        localStorage.setItem(StorageKeys.deviceId, 'one');
        saveTitlePlaybackPreferences(movie, { maxAudioChannels: 6 });
        localStorage.setItem(StorageKeys.deviceId, 'two');
        expect(readTitlePlaybackPreferences(movie).maxAudioChannels).toBeUndefined();
        expect(hasTitlePlaybackPreferences()).toBe(false);
    });
    it('uses the destination server profile and isolates servers', () => {
        serverVault.addOrUpdateServer({ id: 'one', name: 'One', url: 'https://one.test', profileId: 'profile-one', token: '', isAdmin: false });
        serverVault.addOrUpdateServer({ id: 'two', name: 'Two', url: 'https://two.test', profileId: 'profile-two', token: '', isAdmin: false });
        saveTitlePlaybackPreferences(movie, { maxAudioChannels: 6 }, 'one');
        expect(readTitlePlaybackPreferences(movie, 'two').maxAudioChannels).toBeUndefined();
        expect(hasTitlePlaybackPreferences('two')).toBe(false);
        expect(titlePlaybackKey(movie, 'one')).toContain('profile-one');
    });
    it('isolates profiles on the same server and device', () => {
        serverVault.addOrUpdateServer({ id: 'one', name: 'One', url: 'https://one.test', profileId: 'a', token: '', isAdmin: false });
        saveTitlePlaybackPreferences(movie, { bitrate: 4 }, 'one');
        serverVault.addOrUpdateServer({ id: 'one', name: 'One', url: 'https://one.test', profileId: 'b', token: '', isAdmin: false });
        expect(readTitlePlaybackPreferences(movie, 'one').bitrate).toBeUndefined();
    });
    it('reset removes the override and preserves global settings', () => {
        localStorage.setItem('playback_prefs_unknown_unknown', '{"maxAudioChannels":2}');
        saveTitlePlaybackPreferences(movie, { maxAudioChannels: 6 });
        saveTitlePlaybackPreferences(movie, {});
        expect(hasTitlePlaybackPreferences()).toBe(false);
        expect(localStorage.getItem('playback_prefs_unknown_unknown')).toBe('{"maxAudioChannels":2}');
    });
    it.each(['null', '[]', 'true', '{broken', '{"maxAudioChannels":999,"bitrate":-1,"maxResolution":-5,"audio":{}}'])('ignores corrupt preferences: %s', value => {
        localStorage.setItem(titlePlaybackKey(movie), value);
        expect(Object.values(readTitlePlaybackPreferences(movie)).every(item => item === undefined)).toBe(true);
    });
});

describe('title capabilities', () => {
    const raw = { ...caps, requestedMaxResolution: 2160, maxAudioChannels: 2 };
    it('overrides global quality, bitrate and stereo with explicit title preferences', () => {
        const actual = applyTitlePlaybackCapabilities(caps, raw, { maxResolution: 1080, bitrate: 20, maxAudioChannels: 6 });
        expect(actual).toMatchObject({ requestedMaxResolution: 1080, requestedClientBitrateKbps: 20000, maxAudioChannels: 6 });
        expect(actual.audioCodecs).toEqual(caps.audioCodecs);
    });
    it('zero means original quality, unlimited requested bitrate and detected output', () => {
        expect(applyTitlePlaybackCapabilities(caps, raw, { maxResolution: 0, bitrate: 0, maxAudioChannels: 0 })).toMatchObject({ requestedMaxResolution: 2160, requestedClientBitrateKbps: 0, maxAudioChannels: 2 });
    });
    it('absent values preserve global defaults', () => expect(applyTitlePlaybackCapabilities(caps, raw, {})).toEqual(caps));
    it('keeps the physical display limit', () => expect(applyTitlePlaybackCapabilities(caps, { ...raw, requestedMaxResolution: 1080 }, { maxResolution: 2160 }).requestedMaxResolution).toBe(1080));
});

describe('track matching across episodes and rescans', () => {
    it('retains automatic preference for higher quality supported audio', () => {
        const next = { ...movie, mediaParts: [{ ...part, audioTracks: [{ id: 'mp3', codec: 'mp3', channels: 2 }, { id: 'aac', codec: 'aac', channels: 2 }] }] };
        expect(resolveTitlePlayback(next, {}, { ...caps, audioCodecs: ['mp3', 'aac'] }).audio?.id).toBe('aac');
    });
    it('matches saved video version characteristics with new track IDs', () => {
        const next = { ...movie, mediaParts: [{ ...part, id: 'next-part', videoTracks: [{ id: 'next-video', codec: 'h264' }] }] };
        expect(resolveTitlePlayback(next, { video: saveVideoChoice(part, part.videoTracks[0]) }, caps).video?.id).toBe('next-video');
    });
    it('matches surround audio by language and channels after IDs change', () => {
        const next = { ...movie, mediaParts: [{ ...part, audioTracks: [{ id: 'next-stereo', language: 'en', codec: 'aac', channels: 2 }, { id: 'next-surround', language: 'en', codec: 'aac', channels: 6 }] }] };
        const resolved = resolveTitlePlayback(next, { audio: saveAudioChoice(part.audioTracks[0]) }, caps);
        expect(resolved.audio?.id).toBe('next-surround');
        expect(resolved.manualAudio).toBe(true);
    });
    it('preserves a manual unsupported track instead of silently replacing it', () => {
        expect(resolveTitlePlayback(movie, { audio: saveAudioChoice(part.audioTracks[0]) }, caps).audio?.id).toBe('surround');
    });
    it('falls back to automatic audio when the preferred language is unavailable', () => {
        const next = { ...movie, mediaParts: [{ ...part, audioTracks: [{ id: 'french', language: 'fra', codec: 'aac', channels: 2 }] }] };
        const resolved = resolveTitlePlayback(next, { audio: saveAudioChoice(part.audioTracks[0]) }, caps);
        expect(resolved.audio?.id).toBe('french');
        expect(resolved.manualAudio).toBe(false);
    });
    it('does not substitute commentary for dialogue', () => {
        const next = { ...movie, mediaParts: [{ ...part, audioTracks: [{ id: 'commentary', language: 'eng', title: 'Commentary', channels: 2 }] }] };
        expect(resolveTitlePlayback(next, { audio: saveAudioChoice(part.audioTracks[0]) }, caps).manualAudio).toBe(false);
    });
    it('matches subtitles by language, forced and accessibility flags', () => {
        const next = { ...movie, mediaParts: [{ ...part, subtitleTracks: [{ id: 'forced', language: 'en', isForced: true }, { id: 'sdh', language: 'en', isHearingImpaired: true }, { id: 'normal', language: 'en' }] }] };
        expect(resolveTitlePlayback(next, { subtitles: saveSubtitleChoice(part.subtitleTracks[0]) }, caps).subtitleId).toBe('normal');
    });
    it('keeps subtitle off even when the next episode has a default track', () => expect(resolveTitlePlayback(movie, { subtitles: saveSubtitleChoice() }, caps).subtitleId).toBe('none'));
    it('uses off when a requested subtitle language is missing', () => expect(resolveTitlePlayback(movie, { subtitles: { id: 'missing', language: 'jpn' } }, caps).subtitleId).toBe('none'));
    it('falls back from a removed version without sending stale track IDs', () => {
        const selection = resolveTitlePlayback(movie, { video: { id: 'deleted', partId: 'deleted', resolution: '4K' } }, caps);
        expect(selection.video?.id).toBe('video-1');
    });
});
