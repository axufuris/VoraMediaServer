import type { DeviceCapabilities } from './hardwareScanner';

interface VideoTrackLike {
    codec?: string;
    hdrType?: string;
    bitDepth?: number;
    width?: number;
    height?: number;
}

interface PartLike {
    resolution?: string;
    bitrateKbps?: number;
}

interface AudioTrackLike {
    codec?: string;
    channels?: number;
}

// Parse a video height from a resolution string ("4K", "2160p", "1920x1080",
// "1080p", …). Mirrors the backend ParseHeightFromResolution. 0 = unknown,
// treated as within any cap so an unlabeled part isn't forced to transcode.
export function parseResolutionHeight(resolution?: string): number {
    if (!resolution) return 0;
    const r = resolution.toLowerCase();
    if (r.includes('4k') || r.includes('uhd') || r.includes('2160')) return 2160;
    if (r.includes('1440')) return 1440;
    if (r.includes('1080')) return 1080;
    if (r.includes('720')) return 720;
    if (r.includes('576')) return 576;
    if (r.includes('540')) return 540;
    if (r.includes('480')) return 480;
    if (r.includes('360')) return 360;
    if (r.includes('240')) return 240;
    const dims = r.match(/\d+\s*[x×]\s*(\d+)/);
    if (dims) return parseInt(dims[1], 10) || 0;
    return 0;
}

// Normalize an HDR string (analyzer or client canonical) to one token space.
// Any Dolby Vision variant -> DOLBYVISION. SDR/None/unknown -> null.
export function normalizeHdr(raw?: string | null): string | null {
    if (!raw) return null;
    const v = raw.trim().toUpperCase().replace(/[\s_-]/g, '');
    if (v === 'SDR' || v === 'NONE') return null;
    if (v.includes('DOVI') || v.includes('DOLBY')) return 'DOLBYVISION';
    if (v.includes('HDR10PLUS') || v.includes('HDR10+')) return 'HDR10PLUS';
    if (v.includes('HDR10')) return 'HDR10';
    if (v.includes('HLG')) return 'HLG';
    return null;
}

// The client formats that can show a source. A Dolby Vision profile 8 file
// ("DoVi/HDR10", "DoVi/HLG") carries an HDR10 or HLG picture underneath, so a
// screen with that format shows it without Dolby Vision; a bare "DoVi" needs
// Dolby Vision itself. HDR10+ falls back to HDR10. Mirrors the server's
// RenderableHdrFormats, which makes the actual decision.
export function renderableHdrFormats(raw?: string | null): string[] {
    if (!raw) return [];
    const v = raw.trim().toUpperCase().replace(/[\s_-]/g, '');
    if (v === 'SDR' || v === 'NONE') return [];

    const formats = v.includes('DOVI') || v.includes('DOLBY') ? ['DOLBYVISION'] : [];
    if (v.includes('HDR10PLUS') || v.includes('HDR10+')) formats.push('HDR10PLUS', 'HDR10');
    else if (v.includes('HDR10')) formats.push('HDR10');
    if (v.includes('HLG')) formats.push('HLG');
    return formats;
}

function hdrRenderable(sourceHdr: string | undefined, clientFormats: string[] | null | undefined): boolean {
    const renderable = renderableHdrFormats(sourceHdr);
    if (renderable.length === 0 || clientFormats == null) return true;
    const supported = new Set(clientFormats.map(normalizeHdr).filter((x): x is string => x !== null));
    return renderable.some(format => supported.has(format));
}

export function isVideoDirectPlayable(track: VideoTrackLike, part: PartLike, caps: DeviceCapabilities): boolean {
    const codec = track.codec?.toLowerCase() || '';
    if (codec && !caps.videoCodecs.includes(codec)) return false;
    if (caps.maxVideoBitDepth > 0 && (track.bitDepth ?? 0) > caps.maxVideoBitDepth) return false;
    if (!hdrRenderable(track.hdrType, caps.supportedHdrFormats)) return false;
    if (exceedsScreen(track, part, caps)) return false;
    if (caps.requestedClientBitrateKbps > 0 && (part.bitrateKbps ?? 0) > caps.requestedClientBitrateKbps) return false;
    return true;
}

// Mirrors the server: the measured picture against the screen's width and
// height (a client that sent only a height is taken to be widescreen), and the
// file-name label only for a track that was never measured.
function exceedsScreen(track: VideoTrackLike, part: PartLike, caps: DeviceCapabilities): boolean {
    if (caps.requestedMaxResolution <= 0) return false;
    if (track.width && track.height) {
        const maxWidth = caps.requestedMaxWidth > 0 ? caps.requestedMaxWidth : Math.round(caps.requestedMaxResolution * 16 / 9);
        return track.width > maxWidth || track.height > caps.requestedMaxResolution;
    }
    return parseResolutionHeight(part.resolution) > caps.requestedMaxResolution;
}

export function isAudioDirectPlayable(track: AudioTrackLike, caps: DeviceCapabilities): boolean {
    const codec = track.codec?.toLowerCase() || '';
    if (codec && !caps.audioCodecs.includes(codec)) return false;
    if (!caps.playsAnyAudioLayout && (track.channels ?? 2) > caps.maxAudioChannels) return false;
    return true;
}
