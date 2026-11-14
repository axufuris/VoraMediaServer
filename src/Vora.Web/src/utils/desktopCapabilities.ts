import type { DesktopCapabilities } from '../desktop/desktopBridge';
import { desktopPlaybackCapabilities, loadDesktopPlayback } from '../desktop/desktopPlayback';
import {
    applyUserCaps,
    getCurrentProfileDeviceIds,
    readClientBandwidthKbps,
    readPlaybackPrefs,
    readRequestedBitrateKbps,
    scanDeviceCapabilities,
    type DeviceCapabilities,
    type ProfileDeviceIds,
} from './hardwareScanner';

export const DESKTOP_HDR_FORMATS = ['HDR10', 'HLG', 'HDR10+', 'DolbyVision'];

export const DESKTOP_HLS_VIDEO_CODECS = ['h264', 'hevc', 'mpeg2video'];

export function desktopProfile(desktop: DesktopCapabilities, ids?: ProfileDeviceIds): DeviceCapabilities {
    return {
        videoCodecs: desktop.videoCodecs,
        audioCodecs: desktop.audioCodecs,
        containers: desktop.containers,
        maxAudioChannels: desktop.maxAudioChannels,
        detectedAudioChannels: desktop.detectedAudioChannels,
        pickedAudioChannels: 0,
        playsAnyAudioLayout: true,
        canSwitchAudioTracks: true,
        rendersImageSubtitles: true,
        hlsVideoCodecs: DESKTOP_HLS_VIDEO_CODECS,
        clientBandwidthKbps: readClientBandwidthKbps(),
        requestedClientBitrateKbps: readRequestedBitrateKbps(ids),
        requestedMaxResolution: 0,
        requestedMaxWidth: 0,
        displayWidth: desktop.displayWidth,
        displayHeight: desktop.displayHeight,
        measuredBandwidthKbps: 0,
        supportedHdrFormats: DESKTOP_HDR_FORMATS,
        maxVideoBitDepth: 12,
    };
}

export function desktopDeviceCapabilities(desktop: DesktopCapabilities, profileId: string, deviceId: string): DeviceCapabilities {
    return applyUserCaps(desktopProfile(desktop, { profileId, deviceId }), readPlaybackPrefs(profileId, deviceId));
}

export async function playbackCapabilities(serverId?: string): Promise<DeviceCapabilities> {
    await loadDesktopPlayback();
    const desktop = desktopPlaybackCapabilities();
    if (!desktop) return scanDeviceCapabilities(serverId);
    const { profileId, deviceId } = getCurrentProfileDeviceIds(serverId);
    return desktopDeviceCapabilities(desktop, profileId, deviceId);
}
