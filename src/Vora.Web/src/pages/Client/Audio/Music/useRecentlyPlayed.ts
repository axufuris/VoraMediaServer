import { useCallback, useState } from 'react';
import { musicService, type RecentlyPlayedItemVM } from '../../../../api/Music/musicService';
import { useSignalREvent } from '../../../../hooks/useSignalREvent';

export const RECENTLY_PLAYED_LIMIT = 20;

// The Recently Played row's items. The page loads them with its other rows;
// after that the row reloads on its own whenever the server says a play was
// saved for this profile, here or on another device, so a playlist being
// played shows up without leaving the page.
export function useRecentlyPlayed(serverId?: string) {
    const [items, setItems] = useState<RecentlyPlayedItemVM[]>([]);

    const reload = useCallback(() => {
        musicService.getRecentlyPlayedItems(RECENTLY_PLAYED_LIMIT, serverId)
            .then(setItems)
            .catch(err => console.error('Failed to reload Recently Played', err));
    }, [serverId]);

    useSignalREvent<string>('MusicHistoryUpdated', reload);

    return [items, setItems] as const;
}
