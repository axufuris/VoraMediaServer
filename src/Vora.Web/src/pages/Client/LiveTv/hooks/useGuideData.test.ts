import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useGuideData, emptyGuidePrefs } from './useGuideData';
import { serverVault } from '../../../../utils/serverVault';
import { StorageKeys } from '../../../../utils/storageKeys';
import { clearApiClientCache } from '../../../../api/client';

const mocks = vi.hoisted(() => ({
    getPlaylists: vi.fn(),
    getGuide: vi.fn(),
    getRecordingSessions: vi.fn(),
    getIptvPrefs: vi.fn(),
    saveIptvPrefs: vi.fn(),
}));

vi.mock('../../../../api/Iptv/iptvClientService', () => ({
    iptvClientService: { getPlaylists: mocks.getPlaylists, getGuide: mocks.getGuide },
}));
vi.mock('../../../../api/Iptv/dvrService', () => ({
    dvrService: { getRecordingSessions: mocks.getRecordingSessions },
}));
vi.mock('../../../../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getIptvPrefs: mocks.getIptvPrefs, saveIptvPrefs: mocks.saveIptvPrefs },
}));
vi.mock('../../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));

const tokenFor = (profileId: string, accountId = 'user-1') => `header.${btoa(JSON.stringify({ sub: profileId, accountId }))}.signature`;
const start = new Date('2026-11-14T18:00:00Z');
const end = new Date('2026-11-14T22:00:00Z');

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(StorageKeys.profileToken, tokenFor('profile-1'));
    localStorage.setItem(StorageKeys.userId, 'user-1');
    localStorage.setItem(StorageKeys.deviceId, 'device-1');
    serverVault.addOrUpdateServer({ id: 'home', name: 'Home', url: 'http://home', token: tokenFor('profile-1'), profileId: 'profile-1', isAdmin: false });
    serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second', token: tokenFor('profile-2', 'account-2'), profileId: 'profile-2', isAdmin: false });
    serverVault.setActiveServerId('home');
    mocks.getPlaylists.mockResolvedValue([]);
    mocks.getGuide.mockResolvedValue({});
    mocks.getRecordingSessions.mockResolvedValue([]);
    mocks.getIptvPrefs.mockResolvedValue(null);
    mocks.saveIptvPrefs.mockResolvedValue(undefined);
});

afterEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    clearApiClientCache();
});

describe('useGuideData on a second server', () => {
    it("loads the guide, prefs and recordings of that server's own profile", async () => {
        const { result } = renderHook(() => useGuideData('second', start, end));
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(mocks.getPlaylists).toHaveBeenCalledWith('account-2', 'profile-2', 'second');
        expect(mocks.getIptvPrefs).toHaveBeenCalledWith('profile-2', 'device-1', 'second');
        expect(mocks.getGuide).toHaveBeenCalledWith('account-2', 'profile-2', [], start.toISOString(), end.toISOString(), 'second');
        expect(mocks.getRecordingSessions).toHaveBeenCalledWith('profile-2', 'second');
    });

    it("saves guide prefs under that server's own profile", async () => {
        const { result } = renderHook(() => useGuideData('second', start, end));
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        act(() => result.current.updatePrefs({ ...emptyGuidePrefs(), favoriteChannels: ['bbc-one'] }));

        expect(mocks.saveIptvPrefs).toHaveBeenCalledWith('profile-2', 'device-1', expect.any(String), 'second');
        expect(localStorage.getItem(StorageKeys.iptvPrefs('profile-2', 'device-1'))).toContain('bbc-one');
        expect(localStorage.getItem(StorageKeys.iptvPrefs('profile-1', 'device-1'))).toBeNull();
    });

    it('keeps the primary profile on the primary server', async () => {
        const { result } = renderHook(() => useGuideData(undefined, start, end));
        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(mocks.getPlaylists).toHaveBeenCalledWith('user-1', 'profile-1', undefined);
        expect(mocks.getIptvPrefs).toHaveBeenCalledWith('profile-1', 'device-1', undefined);
        expect(mocks.getRecordingSessions).toHaveBeenCalledWith('profile-1', undefined);
    });
});
