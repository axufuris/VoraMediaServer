import { SessionKeys, StorageKeys, getProfileIdFromToken } from '../../../../utils/storageKeys';
import type { MusicNavState } from './musicNavState';

// The Music page restores its view from session storage, so another page can
// open it on an artist or an album by writing the view here and navigating.
export function storeMusicNav(navState: MusicNavState): void {
    try {
        sessionStorage.setItem(SessionKeys.musicNavState, JSON.stringify(navState));
        const profileId = getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken)) ?? '';
        sessionStorage.setItem(SessionKeys.musicNavProfile, profileId);
    } catch {
        /* storage unavailable: the Music page opens on its root view instead */
    }
}
