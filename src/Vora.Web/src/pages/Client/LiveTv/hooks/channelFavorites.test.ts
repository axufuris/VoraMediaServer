import { beforeEach, describe, expect, it, vi } from 'vitest';
import { setChannelFavorite, withFavorite } from './channelFavorites';
import { StorageKeys } from '../../../../utils/storageKeys';

const mocks = vi.hoisted(() => ({
    getIptvPrefs: vi.fn(),
    saveIptvPrefs: vi.fn(),
}));

vi.mock('../../../../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getIptvPrefs: mocks.getIptvPrefs, saveIptvPrefs: mocks.saveIptvPrefs },
}));

beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    mocks.saveIptvPrefs.mockResolvedValue(undefined);
});

describe('withFavorite', () => {
    it('lists a favourite once, at the end, and takes one out', () => {
        expect(withFavorite(['b', 'a'], 'b', true)).toEqual(['a', 'b']);
        expect(withFavorite(['a', 'b'], 'b', false)).toEqual(['a']);
        expect(withFavorite(['a'], 'z', false)).toEqual(['a']);
    });
});

describe('the player heart', () => {
    it('adds the channel on top of the server prefs, keeping the filters', async () => {
        mocks.getIptvPrefs.mockResolvedValue(JSON.stringify({ favoriteChannels: ['a'], hiddenChannels: ['h1'], hideEmpty: true }));

        const saved = await setChannelFavorite('p1', 'd1', 'cnn.us', true, 's1');

        expect(saved).toEqual(['a', 'cnn.us']);
        const [profileId, deviceId, json, serverId] = mocks.saveIptvPrefs.mock.calls[0];
        expect([profileId, deviceId, serverId]).toEqual(['p1', 'd1', 's1']);
        const sent = JSON.parse(json);
        expect(sent.favoriteChannels).toEqual(['a', 'cnn.us']);
        expect(sent.hiddenChannels).toEqual(['h1']);
        expect(sent.hideEmpty).toBe(true);
        expect(localStorage.getItem(StorageKeys.iptvPrefs('p1', 'd1'))).toBe(json);
    });

    it('takes the channel off, and fails when the server can\'t be reached', async () => {
        mocks.getIptvPrefs.mockResolvedValue(JSON.stringify({ favoriteChannels: ['a', 'cnn.us'] }));
        await expect(setChannelFavorite('p1', 'd1', 'cnn.us', false)).resolves.toEqual(['a']);

        mocks.getIptvPrefs.mockRejectedValue(new Error('offline'));
        await expect(setChannelFavorite('p1', 'd1', 'cnn.us', true)).rejects.toThrow('offline');
    });
});
