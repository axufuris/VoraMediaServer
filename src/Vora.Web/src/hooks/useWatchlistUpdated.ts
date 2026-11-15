import { useCallback } from 'react';
import { profileIdForServer } from '../api/client';
import { useSignalREvent } from './useSignalREvent';

/**
 * Calls `onChange` when the server says this page's profile has a different
 * watchlist: `WatchlistUpdated` follows every add and remove, from this device
 * or any other on the profile. Another profile's change is ignored.
 */
export function useWatchlistUpdated(serverId: string | undefined, onChange: () => void) {
    useSignalREvent<string>('WatchlistUpdated', useCallback((changedProfileId: string) => {
        const profileId = profileIdForServer(serverId);
        if (!profileId || typeof changedProfileId !== 'string') return;
        if (changedProfileId.toLowerCase() !== profileId.toLowerCase()) return;
        onChange();
    }, [serverId, onChange]));
}
