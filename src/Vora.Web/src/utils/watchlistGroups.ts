import type { WatchlistItem } from '../api/Watchlist/watchlistService';

export interface WatchlistGroup {
    key: 'moviesInLibrary' | 'showsInLibrary' | 'moviesNotInLibrary' | 'showsNotInLibrary';
    title: string;
    items: WatchlistItem[];
}

const GROUPS: { key: WatchlistGroup['key']; title: string; show: boolean; inLibrary: boolean }[] = [
    { key: 'moviesInLibrary', title: 'Movies in Library', show: false, inLibrary: true },
    { key: 'showsInLibrary', title: 'Shows in Library', show: true, inLibrary: true },
    { key: 'moviesNotInLibrary', title: 'Movies Not Yet in Library', show: false, inLibrary: false },
    { key: 'showsNotInLibrary', title: 'Shows Not Yet in Library', show: true, inLibrary: false },
];

export const isShowEntry = (item: WatchlistItem) => item.type.toLowerCase() === 'tvshow';

export function groupWatchlist(items: WatchlistItem[]): WatchlistGroup[] {
    return GROUPS
        .map(({ key, title, show, inLibrary }) => ({
            key,
            title,
            items: items.filter(item => isShowEntry(item) === show && Boolean(item.mediaItemId) === inLibrary),
        }))
        .filter(group => group.items.length > 0);
}
