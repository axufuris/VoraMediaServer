import { useCallback, useEffect } from 'react';
import { profileDeviceSettingsService } from '../api/Users/profileDeviceSettingsService';
import { announcePlaybackPrefsChanged } from '../utils/playbackPrefsEvents';
import { storePlaybackPrefs } from '../utils/playbackPrefs';
import { StorageKeys } from '../utils/storageKeys';
import { useSignalREvent } from './useSignalREvent';

export function usePlaybackPrefsSync(profileId: string, serverId?: string): void {
    const refresh = useCallback(async () => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!profileId || !deviceId) return;

        try {
            const settings = await profileDeviceSettingsService.getPlaybackSettings(profileId, deviceId, serverId);
            if (storePlaybackPrefs(profileId, deviceId, settings)) announcePlaybackPrefsChanged();
        } catch (error: unknown) {
            console.error('Failed to read the playback settings from the server', error);
        }
    }, [profileId, serverId]);

    useEffect(() => {
        void refresh();
    }, [refresh]);

    useSignalREvent<string | undefined>('ClientSettingsChanged', useCallback((changedProfileId: string | undefined) => {
        if (!changedProfileId || changedProfileId.toLowerCase() === profileId.toLowerCase()) void refresh();
    }, [profileId, refresh]));
}
