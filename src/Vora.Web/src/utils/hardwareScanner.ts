import { StorageKeys, getProfileIdFromToken } from './storageKeys';

export interface DeviceCapabilities {
    videoCodecs: string[];
    audioCodecs: string[];
    containers: string[];
    maxAudioChannels: number;
    clientBandwidthKbps: number;
    requestedClientBitrateKbps: number;
    requestedMaxResolution: number;
    supportedHdrFormats: string[];
    maxVideoBitDepth: number;
}

interface ExtendedWindow extends Window {
    webkitAudioContext?: typeof AudioContext;
}

interface ExtendedNavigator extends Navigator {
    connection?: {
        downlink?: number;
    };
}

interface HdrVideoConfiguration {
    contentType: string;
    width: number;
    height: number;
    bitrate: number;
    framerate: number;
    transferFunction?: string;
    colorGamut?: string;
}

const mseSupports = (mimeType: string): boolean => {
    try {
        return !!(window.MediaSource && MediaSource.isTypeSupported && MediaSource.isTypeSupported(mimeType));
    } catch (error: unknown) {
        console.warn(`MediaSource.isTypeSupported failed for ${mimeType}`, error);
        return false;
    }
};

const displayIsHdr = (): boolean => {
    try {
        return typeof window.matchMedia === 'function' && window.matchMedia('(dynamic-range: high)').matches;
    } catch {
        return false;
    }
};

// HEVC Main10 (10-bit) — the decoder HDR10/HLG playback needs.
const HEVC_MAIN10 = 'video/mp4; codecs="hev1.2.4.L153.B0"';
// Dolby Vision (best-effort, mostly Apple). HDR10+ has no reliable browser
// detection API, so it is intentionally never reported.
const DOLBY_VISION = 'video/mp4; codecs="dvh1.05.06"';

const supportsDolbyVision = (): boolean => mseSupports(DOLBY_VISION);

// Best-effort synchronous fallback: used until the async MediaCapabilities
// probe resolves, and on browsers without navigator.mediaCapabilities. Reports
// nothing unless the display can actually render HDR (dynamic-range: high).
const detectHdrFormatsSync = (): string[] => {
    if (!displayIsHdr()) return [];
    const formats = ['HDR10', 'HLG'];
    if (supportsDolbyVision()) formats.push('DolbyVision');
    return formats;
};

let cachedHdrFormats: string[] | null = null;
let hdrProbeStarted = false;

const probeHdrFormatsAsync = async (): Promise<void> => {
    if (!displayIsHdr()) {
        cachedHdrFormats = [];
        return;
    }

    const mediaCapabilities = window.navigator.mediaCapabilities;
    if (!mediaCapabilities || typeof mediaCapabilities.decodingInfo !== 'function') {
        cachedHdrFormats = detectHdrFormatsSync();
        return;
    }

    const base: HdrVideoConfiguration = {
        contentType: HEVC_MAIN10,
        colorGamut: 'rec2020',
        width: 3840,
        height: 2160,
        bitrate: 20_000_000,
        framerate: 30,
    };

    const formats: string[] = [];
    for (const [format, transferFunction] of [['HDR10', 'pq'], ['HLG', 'hlg']] as const) {
        try {
            const config = { type: 'media-source', video: { ...base, transferFunction } } as MediaDecodingConfiguration;
            const info = await mediaCapabilities.decodingInfo(config);
            if (info.supported) formats.push(format);
        } catch (error: unknown) {
            console.warn(`MediaCapabilities HDR probe failed for ${format}`, error);
        }
    }
    if (supportsDolbyVision()) formats.push('DolbyVision');

    cachedHdrFormats = formats;
};

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

    if (!hdrProbeStarted) {
        hdrProbeStarted = true;
        void probeHdrFormatsAsync();
    }
    const supportedHdrFormats = cachedHdrFormats ?? detectHdrFormatsSync();

    // 10-bit decode capability (independent of whether the display is HDR):
    // HEVC Main10, VP9 Profile 2, or AV1 Main 10-bit.
    const supports10Bit = mseSupports(HEVC_MAIN10)
        || mseSupports('video/webm; codecs="vp09.02.10.10.10.01.09.16.09.00"')
        || mseSupports('video/mp4; codecs="av01.0.08M.10"');
    const maxVideoBitDepth = supports10Bit ? 10 : 8;

    // Auto-detect the display's max resolution and cap the requested output at
    // it. Honor a lower manual preference, but never let a manual cap raise it
    // above what the panel can show. min(width,height) picks the vertical size
    // for both landscape and portrait; DPR converts CSS px to physical px.
    try {
        const displayMaxHeight = Math.round(Math.min(screen.width, screen.height) * (window.devicePixelRatio || 1));
        if (displayMaxHeight > 0) {
            requestedMaxResolution = requestedMaxResolution > 0
                ? Math.min(requestedMaxResolution, displayMaxHeight)
                : displayMaxHeight;
        }
    } catch (error: unknown) {
        console.warn("Could not auto-detect display resolution.", error);
    }

    return { videoCodecs, audioCodecs, containers, maxAudioChannels, clientBandwidthKbps, requestedClientBitrateKbps, requestedMaxResolution, supportedHdrFormats, maxVideoBitDepth };
};