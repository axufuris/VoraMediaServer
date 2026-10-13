import type { AudioTrack, MediaItem, MediaPart, SubtitleTrack, VideoTrack } from '../api/Media/mediaService';
import type { DeviceCapabilities } from './hardwareScanner';
import { isVideoDirectPlayable, parseResolutionHeight } from './playbackDecision';
import { pickBestAudioTrack } from './audioTrackSelection';
import { serverVault } from './serverVault';
import { getProfileIdFromToken, StorageKeys } from './storageKeys';

export interface SavedVideoChoice {
    id: string;
    partId: string;
    resolution?: string;
    edition?: string;
    codec?: string;
    hdrType?: string;
}

export interface SavedAudioChoice {
    id: string;
    language?: string;
    codec?: string;
    channels?: number;
    title?: string;
}

export interface SavedSubtitleChoice {
    id: string;
    language?: string;
    title?: string;
    isForced?: boolean;
    isHearingImpaired?: boolean;
}

export interface TitlePlaybackPreferences {
    video?: SavedVideoChoice;
    audio?: SavedAudioChoice;
    subtitles?: SavedSubtitleChoice;
    maxResolution?: number;
    bitrate?: number;
    maxAudioChannels?: number;
}

export function titlePlaybackId(media: Pick<MediaItem, 'id' | 'type' | 'tvShowId'>): string {
    return media.type === 'Episode' || media.type === 'Season' ? media.tvShowId || media.id : media.id;
}

function preferenceScope(serverId?: string) {
    const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
    const profileId = server?.profileId || getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken)) || 'unknown';
    const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
    return { server: server?.url || serverId || window.location.origin, profileId, deviceId };
}

export function titlePlaybackKey(media: Pick<MediaItem, 'id' | 'type' | 'tvShowId'>, serverId?: string): string {
    const scope = preferenceScope(serverId);
    return StorageKeys.titlePlayback(scope.server, scope.profileId, scope.deviceId, titlePlaybackId(media));
}

export function hasTitlePlaybackPreferences(serverId?: string): boolean {
    const scope = preferenceScope(serverId);
    const prefix = `title_playback_${JSON.stringify([scope.server, scope.profileId, scope.deviceId]).slice(0, -1)},`;
    for (let i = 0; i < localStorage.length; i++) {
        if (localStorage.key(i)?.startsWith(prefix)) return true;
    }
    return false;
}

export function readTitlePlaybackPreferences(media: Pick<MediaItem, 'id' | 'type' | 'tvShowId'>, serverId?: string): TitlePlaybackPreferences {
    try {
        const saved = JSON.parse(localStorage.getItem(titlePlaybackKey(media, serverId)) || '{}') as TitlePlaybackPreferences;
        if (!saved || typeof saved !== 'object' || Array.isArray(saved)) return {};
        return {
            video: saved.video && typeof saved.video.id === 'string' && typeof saved.video.partId === 'string' ? saved.video : undefined,
            audio: saved.audio && typeof saved.audio.id === 'string' ? saved.audio : undefined,
            subtitles: saved.subtitles && typeof saved.subtitles.id === 'string' ? saved.subtitles : undefined,
            maxResolution: [0, 2160, 1080, 720, 480].includes(saved.maxResolution ?? -1) ? saved.maxResolution : undefined,
            bitrate: [0, 40, 20, 10, 4].includes(saved.bitrate ?? -1) ? saved.bitrate : undefined,
            maxAudioChannels: [0, 2, 6, 8].includes(saved.maxAudioChannels ?? -1) ? saved.maxAudioChannels : undefined,
        };
    } catch {
        return {};
    }
}

export function saveTitlePlaybackPreferences(media: Pick<MediaItem, 'id' | 'type' | 'tvShowId'>, preferences: TitlePlaybackPreferences, serverId?: string): void {
    const key = titlePlaybackKey(media, serverId);
    if (Object.values(preferences).every(value => value === undefined)) localStorage.removeItem(key);
    else localStorage.setItem(key, JSON.stringify(preferences));
    window.dispatchEvent(new Event('title-playback-preferences-changed'));
}

export function applyTitlePlaybackCapabilities(caps: DeviceCapabilities, raw: DeviceCapabilities, preferences: TitlePlaybackPreferences): DeviceCapabilities {
    return {
        ...caps,
        requestedMaxResolution: preferences.maxResolution === undefined ? caps.requestedMaxResolution
            : preferences.maxResolution === 0 ? raw.requestedMaxResolution : Math.min(preferences.maxResolution, raw.requestedMaxResolution || preferences.maxResolution),
        requestedClientBitrateKbps: preferences.bitrate === undefined ? caps.requestedClientBitrateKbps : preferences.bitrate * 1000,
        maxAudioChannels: preferences.maxAudioChannels === undefined ? caps.maxAudioChannels : preferences.maxAudioChannels || raw.maxAudioChannels,
    };
}

function normalized(value?: string): string {
    return typeof value === 'string' ? value.trim().toLowerCase() : '';
}

function language(value?: string): string {
    const code = normalized(value);
    if (!code || code === 'und') return '';
    try { return new Intl.Locale(code).language; } catch { return code; }
}

export function saveVideoChoice(part: MediaPart, track: VideoTrack): SavedVideoChoice {
    return { id: track.id, partId: part.id, resolution: part.resolution, edition: part.edition, codec: track.codec, hdrType: track.hdrType };
}

export function saveAudioChoice(track: AudioTrack): SavedAudioChoice {
    return { id: track.id, language: track.language, codec: track.codec, channels: track.channels, title: track.title };
}

export function saveSubtitleChoice(track?: SubtitleTrack): SavedSubtitleChoice {
    return track ? { id: track.id, language: track.language, title: track.title, isForced: !!track.isForced, isHearingImpaired: !!track.isHearingImpaired } : { id: 'none' };
}

export function resolveAudioChoice(part: MediaPart | undefined, choice: SavedAudioChoice | undefined): AudioTrack | undefined {
    const tracks = part?.audioTracks ?? [];
    const exact = tracks.find(track => track.id === choice?.id);
    if (exact || !choice) return exact;
    const matches = tracks.filter(track => language(track.language) === language(choice.language)
        && normalized(track.title).includes('commentary') === normalized(choice.title).includes('commentary'));
    return [...matches].sort((a, b) => {
        const score = (track: AudioTrack) => (normalized(track.title) === normalized(choice.title) ? 8 : 0)
            + (track.channels === choice.channels ? 4 : 0) + (normalized(track.codec) === normalized(choice.codec) ? 2 : 0);
        return score(b) - score(a);
    })[0];
}

export function resolveSubtitleChoice(part: MediaPart | undefined, choice: SavedSubtitleChoice | undefined): string {
    const tracks = part?.subtitleTracks ?? [];
    if (!choice) return tracks.find(track => track.isForced || track.isDefault)?.id ?? 'none';
    if (choice.id === 'none') return 'none';
    const exact = tracks.find(track => track.id === choice.id);
    if (exact) return exact.id;
    const matches = tracks.filter(track => language(track.language) === language(choice.language)
        && !!track.isForced === !!choice.isForced && !!track.isHearingImpaired === !!choice.isHearingImpaired);
    return matches.find(track => normalized(track.title) === normalized(choice.title))?.id ?? matches[0]?.id ?? 'none';
}

export function resolveTitlePlayback(media: MediaItem, preferences: TitlePlaybackPreferences, caps: DeviceCapabilities, preferredPart?: MediaPart) {
    const parts = media.mediaParts ?? [];
    const videos = parts.flatMap(part => (part.videoTracks ?? []).map(track => ({ part, track })));
    const savedVideo = videos.find(video => video.track.id === preferences.video?.id)
        ?? (preferences.video ? videos.find(video => parseResolutionHeight(video.part.resolution) === parseResolutionHeight(preferences.video?.resolution)
            && normalized(video.part.edition) === normalized(preferences.video?.edition)
            && normalized(video.track.codec) === normalized(preferences.video?.codec)
            && normalized(video.track.hdrType) === normalized(preferences.video?.hdrType)) : undefined);
    const fitting = parts.filter(part => !caps.requestedMaxResolution || parseResolutionHeight(part.resolution) <= caps.requestedMaxResolution);
    const defaultPart = [...(fitting.length ? fitting : parts)].sort((a, b) => parseResolutionHeight(b.resolution) - parseResolutionHeight(a.resolution))[0];
    const part = preferredPart ?? savedVideo?.part ?? defaultPart;
    const video = preferredPart ? part?.videoTracks?.find(track => track.isDefault) ?? part?.videoTracks?.[0]
        : savedVideo?.track ?? part?.videoTracks?.find(track => isVideoDirectPlayable(track, part, caps))
            ?? part?.videoTracks?.find(track => track.isDefault) ?? part?.videoTracks?.[0];
    const manualAudio = resolveAudioChoice(part, preferences.audio);
    const audio = manualAudio ?? pickBestAudioTrack(part?.audioTracks, caps);
    return { part, video, audio, subtitleId: resolveSubtitleChoice(part, preferences.subtitles), manualAudio: !!manualAudio };
}
