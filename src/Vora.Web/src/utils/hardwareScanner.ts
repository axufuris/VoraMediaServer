import { StorageKeys, getProfileIdFromToken } from './storageKeys';

export interface DeviceCapabilities {
    videoCodecs: string[];
    audioCodecs: string[];
    containers: string[];
    maxAudioChannels: number;
    clientBandwidthKbps: number;
    requestedClientBitrateKbps: number;
    requestedMaxResolution: number;
}

interface ExtendedWindow extends Window {
    webkitAudioContext?: typeof AudioContext;
}

interface ExtendedNavigator extends Navigator {
    connection?: {
        downlink?: number;
    };
}

export const scanDeviceCapabilities = (): DeviceCapabilities => {
    const videoCodecs: string[] = [];
    const audioCodecs: string[] = [];
    const containers: string[] = [];
    let maxAudioChannels = 2;
    let clientBandwidthKbps = 100000;

    let requestedClientBitrateKbps = 0;
    let requestedMaxResolution = 0;
    let requestedMaxAudioChannels = 0;

    try {
        const token = localStorage.getItem(StorageKeys.profileToken);
        if (token) {
            const profileId = getProfileIdFromToken(token);
            const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
            const savedPref = localStorage.getItem(`playback_prefs_${profileId}_${deviceId}`);

            if (savedPref) {
                try {
                    const parsed = JSON.parse(savedPref);
                    if (parsed.bitrate) requestedClientBitrateKbps = parsed.bitrate * 1000;
                    if (parsed.maxResolution !== undefined) requestedMaxResolution = parsed.maxResolution;
                    if (parsed.maxAudioChannels !== undefined) requestedMaxAudioChannels = parsed.maxAudioChannels;
                } catch (error: unknown) {
                    console.warn("Failed to parse playback prefs JSON, falling back to legacy format.", error);
                    requestedClientBitrateKbps = parseInt(savedPref, 10) * 1000 || 0;
                }
            }
        }
    } catch (error: unknown) {
        console.warn("Could not read client playback preferences.", error);
    }

    try {
        const nav = window.navigator as ExtendedNavigator;
        if (nav.connection?.downlink) {
            clientBandwidthKbps = Math.round(nav.connection.downlink * 1000);
        }
    } catch (error: unknown) {
        console.warn("Could not check client bandwidth.", error);
    }

    try {
        const extWindow = window as ExtendedWindow;
        const AudioCtx = window.AudioContext || extWindow.webkitAudioContext;
        if (AudioCtx) {
            const ctx = new AudioCtx();
            maxAudioChannels = ctx.destination.maxChannelCount || 2;
            ctx.close().catch((err: unknown) => console.warn("Failed to close audio context", err));
        }
    } catch (error: unknown) {
        console.warn("Could not check audio capabilities.", error);
    }

    if (requestedMaxAudioChannels > 0) {
        maxAudioChannels = requestedMaxAudioChannels;
    }

    const videoTests = {
        'h264': 'video/mp4; codecs="avc1.42E01E"',
        'hevc': 'video/mp4; codecs="hev1.1.6.L93.B0"',
        'vp9': 'video/webm; codecs="vp09.00.10.08"',
        'av1': 'video/mp4; codecs="av01.0.04M.08"'
    };

    const audioTests = {
        'aac': 'audio/mp4; codecs="mp4a.40.2"',
        'ac3': 'audio/mp4; codecs="ac-3"',
        'eac3': 'audio/mp4; codecs="ec-3"',
        'opus': 'audio/webm; codecs="opus"',
        'flac': 'audio/flac',
        'mp3': 'audio/mpeg'
    };

    const containerTests = {
        'mp4': 'video/mp4',
        'webm': 'video/webm',
        'mkv': 'video/x-matroska',
        'hls': 'application/x-mpegURL'
    };

    const checkCodecSupport = (mimeType: string) => {
        const video = document.createElement('video');
        const canPlay = video.canPlayType(mimeType);
        if (canPlay === 'probably' || canPlay === 'maybe') return true;

        if (window.MediaSource && MediaSource.isTypeSupported) {
            try {
                return MediaSource.isTypeSupported(mimeType);
            } catch (error: unknown) {
                console.warn(`MediaSource check failed for ${mimeType}`, error);
                return false;
            }
        }
        return false;
    };

    const checkContainerSupport = (mimeType: string) => {
        const video = document.createElement('video');
        const result = video.canPlayType(mimeType);
        return result === 'probably' || result === 'maybe';
    };

    for (const [codec, mime] of Object.entries(videoTests)) {
        if (checkCodecSupport(mime)) videoCodecs.push(codec);
    }
    for (const [codec, mime] of Object.entries(audioTests)) {
        if (checkCodecSupport(mime)) audioCodecs.push(codec);
    }
    for (const [container, mime] of Object.entries(containerTests)) {
        if (checkContainerSupport(mime)) containers.push(container);
    }

    const video = document.createElement('video');
    if (video.canPlayType('application/vnd.apple.mpegurl')) {
        if (!containers.includes('hls')) containers.push('hls');
    }

    return { videoCodecs, audioCodecs, containers, maxAudioChannels, clientBandwidthKbps, requestedClientBitrateKbps, requestedMaxResolution };
};