import { StorageKeys } from '../../../../utils/storageKeys';

export type MusicSubTab = 'forYou' | 'artists' | 'albums' | 'playlists';

export const MUSIC_SUB_TABS: { key: MusicSubTab; label: string }[] = [
    { key: 'forYou', label: 'For You' },
    { key: 'artists', label: 'Artists' },
    { key: 'albums', label: 'Albums' },
    { key: 'playlists', label: 'Playlists' },
];

const isMusicSubTab = (value: string | null): value is MusicSubTab =>
    MUSIC_SUB_TABS.some(t => t.key === value);

export function readMusicSubTab(): MusicSubTab {
    try {
        const saved = localStorage.getItem(StorageKeys.musicSubTab);
        return isMusicSubTab(saved) ? saved : 'forYou';
    } catch {
        return 'forYou';
    }
}

export function saveMusicSubTab(tab: MusicSubTab): void {
    try {
        localStorage.setItem(StorageKeys.musicSubTab, tab);
    } catch {
        /* storage unavailable — the tab just won't be remembered */
    }
}

export function musicSubTabLabel(tab: MusicSubTab): string {
    return MUSIC_SUB_TABS.find(t => t.key === tab)?.label ?? 'For You';
}
