import { StorageKeys } from './storageKeys';

export interface StoredPlaybackPrefs {
    bitrate: number;
    maxResolution: number;
    maxAudioChannels: number;
    seekStepSeconds: number;
    volumeStepPercent: number;
}

export function serializePlaybackPrefs(prefs: StoredPlaybackPrefs): string {
    return JSON.stringify({
        bitrate: prefs.bitrate,
        maxResolution: prefs.maxResolution,
        maxAudioChannels: prefs.maxAudioChannels,
        seekStepSeconds: prefs.seekStepSeconds,
        volumeStepPercent: prefs.volumeStepPercent,
    });
}

export function storePlaybackPrefs(profileId: string, deviceId: string, prefs: StoredPlaybackPrefs): boolean {
    const key = StorageKeys.playbackPrefs(profileId, deviceId);
    const next = serializePlaybackPrefs(prefs);
    if (localStorage.getItem(key) === next) return false;
    localStorage.setItem(key, next);
    return true;
}
