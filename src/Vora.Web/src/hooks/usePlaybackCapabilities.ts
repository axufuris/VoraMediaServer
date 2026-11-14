import { useCallback, useEffect, useSyncExternalStore } from 'react';
import type { DesktopCapabilities } from '../desktop/desktopBridge';
import { desktopPlaybackCapabilities, loadDesktopPlayback, subscribeDesktopPlayback } from '../desktop/desktopPlayback';
import { desktopDeviceCapabilities } from '../utils/desktopCapabilities';
import { getCurrentProfileDeviceIds, getEffectiveCapabilities, type DeviceCapabilities } from '../utils/hardwareScanner';
import { onPlaybackPrefsChanged } from '../utils/playbackPrefsEvents';
import { StorageKeys } from '../utils/storageKeys';

interface CapabilitiesSnapshot {
    savedPrefs: string | null;
    desktop: DesktopCapabilities | null;
    capabilities: DeviceCapabilities;
}

const snapshots = new Map<string, CapabilitiesSnapshot>();

function currentCapabilities(serverId?: string): DeviceCapabilities {
    const { profileId, deviceId } = getCurrentProfileDeviceIds(serverId);
    const key = `${profileId}|${deviceId}`;
    const savedPrefs = localStorage.getItem(StorageKeys.playbackPrefs(profileId, deviceId));
    const desktop = desktopPlaybackCapabilities();
    const snapshot = snapshots.get(key);
    if (snapshot && snapshot.savedPrefs === savedPrefs && snapshot.desktop === desktop) {
        return snapshot.capabilities;
    }
    const capabilities = desktop ? desktopDeviceCapabilities(desktop, profileId, deviceId) : getEffectiveCapabilities(profileId, deviceId);
    snapshots.set(key, { savedPrefs, desktop, capabilities });
    return capabilities;
}

function subscribe(onChange: () => void): () => void {
    const stopDesktop = subscribeDesktopPlayback(onChange);
    const stopPrefs = onPlaybackPrefsChanged(onChange);
    return () => {
        stopDesktop();
        stopPrefs();
    };
}

export function usePlaybackCapabilities(serverId?: string): DeviceCapabilities {
    useEffect(() => { void loadDesktopPlayback(); }, []);
    const read = useCallback(() => currentCapabilities(serverId), [serverId]);
    return useSyncExternalStore(subscribe, read, read);
}
