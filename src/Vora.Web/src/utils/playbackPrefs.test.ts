import { beforeEach, describe, expect, it } from 'vitest';
import { serializePlaybackPrefs, storePlaybackPrefs, type StoredPlaybackPrefs } from './playbackPrefs';

const prefs: StoredPlaybackPrefs = { bitrate: 20, maxResolution: 1080, maxAudioChannels: 6, seekStepSeconds: 30, volumeStepPercent: 2 };

beforeEach(() => localStorage.clear());

describe('playback prefs kept on this device', () => {
    it('are the five settings the readers look for, in one order', () => {
        expect(serializePlaybackPrefs(prefs)).toBe('{"bitrate":20,"maxResolution":1080,"maxAudioChannels":6,"seekStepSeconds":30,"volumeStepPercent":2}');
    });

    it('leave out whatever else the server sends beside them', () => {
        const withSounds = { ...prefs, soundOnClick: false, soundOnMove: false };

        expect(serializePlaybackPrefs(withSounds)).toBe(serializePlaybackPrefs(prefs));
    });

    it('are stored under the profile and device, and say whether that changed anything', () => {
        expect(storePlaybackPrefs('p', 'd', prefs)).toBe(true);
        expect(localStorage.getItem('playback_prefs_p_d')).toBe(serializePlaybackPrefs(prefs));
        expect(storePlaybackPrefs('p', 'd', prefs)).toBe(false);
        expect(storePlaybackPrefs('p', 'd', { ...prefs, bitrate: 0 })).toBe(true);
    });

    it('replace the bare 0 an older build wrote when the server had nothing', () => {
        localStorage.setItem('playback_prefs_p_d', '0');

        expect(storePlaybackPrefs('p', 'd', prefs)).toBe(true);
        expect(JSON.parse(localStorage.getItem('playback_prefs_p_d') ?? '')).toEqual(prefs);
    });
});
