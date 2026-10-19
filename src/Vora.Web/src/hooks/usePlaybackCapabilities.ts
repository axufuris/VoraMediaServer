import { useMemo } from 'react';
import { useDesktopPlayback } from '../desktop/useDesktopPlayback';
import { playsVideoInMpv } from '../desktop/desktopPlayback';
import { desktopDeviceCapabilities } from '../utils/desktopCapabilities';
import { getCurrentProfileDeviceIds, getEffectiveCapabilities, type DeviceCapabilities } from '../utils/hardwareScanner';

export function usePlaybackCapabilities(): DeviceCapabilities {
    const desktop = useDesktopPlayback();
    const desktopCapabilities = playsVideoInMpv(desktop) ? desktop.capabilities : null;
    return useMemo(() => {
        const { profileId, deviceId } = getCurrentProfileDeviceIds();
        return desktopCapabilities
            ? desktopDeviceCapabilities(desktopCapabilities, profileId, deviceId)
            : getEffectiveCapabilities(profileId, deviceId);
    }, [desktopCapabilities]);
}
