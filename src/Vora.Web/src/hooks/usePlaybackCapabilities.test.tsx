import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { usePlaybackCapabilities } from './usePlaybackCapabilities';
import { announcePlaybackPrefsChanged } from '../utils/playbackPrefsEvents';
import { StorageKeys } from '../utils/storageKeys';
import { serverVault } from '../utils/serverVault';
import PlaybackTab from '../pages/Client/Settings/PlaybackTab';
import { DialogProvider } from '../dialogs';

vi.mock('../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { saveClientSettings: vi.fn().mockResolvedValue(undefined) },
}));

vi.mock('../api/Users/profileService', () => ({
    profileService: {
        getMyTrackPreferences: vi.fn().mockResolvedValue({ preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Smart' }),
    },
}));

const prefsKey = StorageKeys.playbackPrefs('unknown', 'device-1');

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('the capabilities the player and the details page judge with', () => {
    it('follow a Playback limit saved while they are on screen', () => {
        const { result } = renderHook(() => usePlaybackCapabilities());
        expect(result.current.requestedMaxResolution).toBe(0);

        act(() => {
            localStorage.setItem(prefsKey, JSON.stringify({ bitrate: 20, maxResolution: 1080, maxAudioChannels: 2 }));
            announcePlaybackPrefsChanged();
        });

        expect(result.current.requestedMaxResolution).toBe(1080);
        expect(result.current.requestedClientBitrateKbps).toBe(20000);
        expect(result.current.pickedAudioChannels).toBe(2);
    });

    it('follow a change saved in another tab', () => {
        const { result } = renderHook(() => usePlaybackCapabilities());

        act(() => {
            localStorage.setItem(prefsKey, JSON.stringify({ maxResolution: 720 }));
            window.dispatchEvent(new StorageEvent('storage', { key: prefsKey }));
        });

        expect(result.current.requestedMaxResolution).toBe(720);
    });

    it('stay the same object while nothing changed', () => {
        const { result, rerender } = renderHook(() => usePlaybackCapabilities());
        const first = result.current;

        rerender();
        act(() => announcePlaybackPrefsChanged());

        expect(result.current).toBe(first);
    });

    it('take the limits the Playback tab saves', async () => {
        const { result } = renderHook(() => usePlaybackCapabilities());
        render(<DialogProvider><PlaybackTab activeProfileId="unknown" onSaved={() => undefined} /></DialogProvider>);

        fireEvent.change(screen.getByDisplayValue('Original'), { target: { value: '1080' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save playback settings' }));

        await waitFor(() => expect(result.current.requestedMaxResolution).toBe(1080));
    });
});

describe('the capabilities for media from a second server', () => {
    const tokenFor = (profileId: string) => `header.${btoa(JSON.stringify({ sub: profileId }))}.signature`;

    beforeEach(() => {
        localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1'));
        serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1'), profileId: 'profile-1', isAdmin: false });
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2'), profileId: 'profile-2', isAdmin: false });
        serverVault.setActiveServerId('home');
        localStorage.setItem(StorageKeys.playbackPrefs('profile-1', 'device-1'), JSON.stringify({ maxResolution: 720 }));
        localStorage.setItem(StorageKeys.playbackPrefs('profile-2', 'device-1'), JSON.stringify({ maxResolution: 1080 }));
    });

    it("judge with that server's profile prefs beside a primary player, each staying the same object", () => {
        const { result, rerender } = renderHook(() => ({ primary: usePlaybackCapabilities(), second: usePlaybackCapabilities('second') }));
        const first = result.current;

        rerender();

        expect(first.primary.requestedMaxResolution).toBe(720);
        expect(first.second.requestedMaxResolution).toBe(1080);
        expect(result.current.primary).toBe(first.primary);
        expect(result.current.second).toBe(first.second);
    });
});
