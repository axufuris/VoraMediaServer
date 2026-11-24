import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { usePlaybackPrefsSync } from './usePlaybackPrefsSync';
import { StorageKeys } from '../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getPlaybackSettings: vi.fn(),
    announce: vi.fn(),
    event: { current: null as ((profileId: string | undefined) => void) | null },
}));

vi.mock('../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getPlaybackSettings: mocks.getPlaybackSettings },
}));
vi.mock('../utils/playbackPrefsEvents', () => ({ announcePlaybackPrefsChanged: mocks.announce }));
vi.mock('./useSignalREvent', () => ({
    useSignalREvent: (_name: string, callback: (profileId: string | undefined) => void) => { mocks.event.current = callback; },
}));

const settings = { bitrate: 10, maxResolution: 720, maxAudioChannels: 2, seekStepSeconds: 15, volumeStepPercent: 10, soundOnClick: true, soundOnNavOpen: true, soundOnMove: true };
const stored = '{"bitrate":10,"maxResolution":720,"maxAudioChannels":2,"seekStepSeconds":15,"volumeStepPercent":10}';

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device');
    mocks.getPlaybackSettings.mockReset().mockResolvedValue(settings);
    mocks.announce.mockReset();
    mocks.event.current = null;
});

afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
});

describe('usePlaybackPrefsSync', () => {
    it('keeps the copy on this device as the server has it, and says when that changed', async () => {
        renderHook(() => usePlaybackPrefsSync('profile', 'second'));

        await waitFor(() => expect(localStorage.getItem('playback_prefs_profile_device')).toBe(stored));
        expect(mocks.getPlaybackSettings).toHaveBeenCalledWith('profile', 'device', 'second');
        expect(mocks.announce).toHaveBeenCalledTimes(1);
    });

    it('replaces a copy that disagrees, and a bare 0 left by an older build', async () => {
        localStorage.setItem('playback_prefs_profile_device', '0');

        renderHook(() => usePlaybackPrefsSync('profile'));

        await waitFor(() => expect(localStorage.getItem('playback_prefs_profile_device')).toBe(stored));
    });

    it('stays quiet when the copy already matches', async () => {
        localStorage.setItem('playback_prefs_profile_device', stored);

        renderHook(() => usePlaybackPrefsSync('profile'));

        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalled());
        expect(mocks.announce).not.toHaveBeenCalled();
    });

    it('keeps what the device has when the server cannot be read', async () => {
        localStorage.setItem('playback_prefs_profile_device', '{"bitrate":4}');
        mocks.getPlaybackSettings.mockRejectedValue(new Error('offline'));
        vi.spyOn(console, 'error').mockImplementation(() => { });

        renderHook(() => usePlaybackPrefsSync('profile'));

        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalled());
        expect(localStorage.getItem('playback_prefs_profile_device')).toBe('{"bitrate":4}');
    });

    it('reads nothing without a profile or a device id', async () => {
        renderHook(() => usePlaybackPrefsSync(''));
        localStorage.removeItem(StorageKeys.deviceId);
        renderHook(() => usePlaybackPrefsSync('profile'));

        await Promise.resolve();
        expect(mocks.getPlaybackSettings).not.toHaveBeenCalled();
    });

    it('reads again when an admin changes this profile, and for a change to the defaults', async () => {
        renderHook(() => usePlaybackPrefsSync('Profile'));
        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalledTimes(1));

        mocks.event.current?.('PROFILE');
        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalledTimes(2));
        mocks.event.current?.(undefined);
        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalledTimes(3));
    });

    it('ignores the change to another profile', async () => {
        renderHook(() => usePlaybackPrefsSync('profile'));
        await waitFor(() => expect(mocks.getPlaybackSettings).toHaveBeenCalledTimes(1));

        mocks.event.current?.('someone-else');
        await Promise.resolve();

        expect(mocks.getPlaybackSettings).toHaveBeenCalledTimes(1);
    });
});
