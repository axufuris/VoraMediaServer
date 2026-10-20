import { useEffect, useSyncExternalStore } from 'react';
import type { DesktopCapabilities } from '../desktop/desktopBridge';
import { desktopPlaybackCapabilities, loadDesktopPlayback, subscribeDesktopPlayback } from '../desktop/desktopPlayback';
import { desktopDeviceCapabilities } from '../utils/desktopCapabilities';
import { getCurrentProfileDeviceIds, getEffectiveCapabilities, type DeviceCapabilities } from '../utils/hardwareScanner';
import { onPlaybackPrefsChanged } from '../utils/playbackPrefsEvents';
import { StorageKeys } from '../utils/storageKeys';

interface CapabilitiesSnapshot {
    profileId: string;
    deviceId: string;
    savedPrefs: string | null;
    desktop: DesktopCapabilities | null;
    capabilities: DeviceCapabilities;
}

let snapshot: CapabilitiesSnapshot | null = null;

function currentCapabilities(): DeviceCapabilities {
    const { profileId, deviceId } = getCurrentProfileDeviceIds();
    const savedPrefs = localStorage.getItem(StorageKeys.playbackPrefs(profileId, deviceId));
    const desktop = desktopPlaybackCapabilities();
    if (snapshot && snapshot.profileId === profileId && snapshot.deviceId === deviceId && snapshot.savedPrefs === savedPrefs && snapshot.desktop === desktop) {
        return snapshot.capabilities;
    }
    const capabilities = desktop ? desktopDeviceCapabilities(desktop, profileId, deviceId) : getEffectiveCapabilities(profileId, deviceId);
    snapshot = { profileId, deviceId, savedPrefs, desktop, capabilities };
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

export function usePlaybackCapabilities(): DeviceCapabilities {
    useEffect(() => { void loadDesktopPlayback(); }, []);
    return useSyncExternalStore(subscribe, currentCapabilities, currentCapabilities);
}
