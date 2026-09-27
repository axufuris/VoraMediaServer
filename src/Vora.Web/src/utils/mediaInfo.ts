// Formatting for the Media Info dialog, shared by the video player and the
// media details page so both describe a file the same way.

export function formatInfoDuration(seconds?: number | null): string | null {
    if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return null;
    const whole = Math.round(seconds);
    const h = Math.floor(whole / 3600);
    const m = Math.floor((whole % 3600) / 60);
    const s = whole % 60;
    const ss = String(s).padStart(2, '0');
    return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

const SizeUnits = ['B', 'KB', 'MB', 'GB', 'TB'];

export function formatFileSize(bytes?: number | null): string | null {
    if (bytes == null || !Number.isFinite(bytes) || bytes <= 0) return null;
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < SizeUnits.length - 1) {
        value /= 1024;
        unit++;
    }
    return `${unit === 0 ? value : value.toFixed(2)} ${SizeUnits[unit]}`;
}

export function formatBitrate(kbps?: number | null): string | null {
    if (kbps == null || !Number.isFinite(kbps) || kbps <= 0) return null;
    return `${Math.round(kbps).toLocaleString('en-US')} kbps`;
}

export function formatChannels(channels?: number | null): string | null {
    if (channels == null || channels <= 0) return null;
    switch (channels) {
        case 1: return 'Mono';
        case 2: return 'Stereo';
        case 6: return '5.1';
        case 8: return '7.1';
        default: return `${channels} channels`;
    }
}

export function formatResolution(resolution?: string | null): string | null {
    if (!resolution) return null;
    return resolution.toLowerCase() === '2160p' ? '4K' : resolution;
}

// ISO 639-2 codes as ffprobe reports them ("eng"), or 639-1 ("en"). Shown as the
// language name with the code beside it, falling back to the code alone when
// the runtime can't name it.
export function formatLanguage(code?: string | null): string | null {
    const trimmed = code?.trim();
    if (!trimmed) return null;
    if (trimmed.toLowerCase() === 'und') return 'Undetermined';

    try {
        const name = new Intl.DisplayNames(['en'], { type: 'language' }).of(trimmed.toLowerCase());
        return name && name.toLowerCase() !== trimmed.toLowerCase() ? `${name} (${trimmed})` : trimmed;
    } catch {
        return trimmed;
    }
}

export function formatCodec(codec?: string | null): string | null {
    const trimmed = codec?.trim();
    return trimmed ? trimmed.toUpperCase() : null;
}

export function fileNameOf(path?: string | null): string | null {
    if (!path) return null;
    const segments = path.split(/[\\/]/);
    return segments[segments.length - 1] || null;
}

export function subtitleSource(track: { isExternal?: boolean; isDownloaded?: boolean }): string {
    if (track.isDownloaded) return 'Downloaded';
    return track.isExternal ? 'Sidecar file' : 'Embedded';
}
