import { describe, it, expect, beforeEach } from 'vitest';
import { parseMusicNavState } from './musicNavState';
import { readMusicSubTab, saveMusicSubTab } from './musicSubTab';
import { StorageKeys } from '../../../../utils/storageKeys';

describe('parseMusicNavState', () => {
    it('restores a stored drill-in view', () => {
        expect(parseMusicNavState('{"view":"album","albumId":"a1","artistId":"r1"}'))
            .toEqual({ view: 'album', albumId: 'a1', artistId: 'r1' });
    });

    it('sends the old hub view back to the sub-tab root', () => {
        expect(parseMusicNavState('{"view":"artists"}')).toEqual({ view: 'root' });
    });

    it('falls back to the root on unreadable state', () => {
        expect(parseMusicNavState('not json')).toEqual({ view: 'root' });
        expect(parseMusicNavState('{}')).toEqual({ view: 'root' });
    });
});

describe('music sub-tab persistence', () => {
    beforeEach(() => localStorage.clear());

    it('defaults to For You', () => {
        expect(readMusicSubTab()).toBe('forYou');
    });

    it('restores the last selected sub-tab', () => {
        saveMusicSubTab('albums');

        expect(localStorage.getItem(StorageKeys.musicSubTab)).toBe('albums');
        expect(readMusicSubTab()).toBe('albums');
    });

    it('ignores a value that is not a sub-tab', () => {
        localStorage.setItem(StorageKeys.musicSubTab, 'genres');

        expect(readMusicSubTab()).toBe('forYou');
    });
});
