import type { DeviceCapabilities } from './hardwareScanner';
import { isAudioDirectPlayable } from './playbackDecision';

interface AudioTrackChoice {
    id: string;
    codec?: string;
    channels?: number;
    language?: string;
    title?: string;
    isDefault?: boolean;
}

const codecQuality = (codec?: string): number => {
    switch (codec?.toLowerCase()) {
        case 'truehd': return 6;
        case 'flac':
        case 'dts': return 5;
        case 'eac3': return 4;
        case 'ac3': return 3;
        case 'aac':
        case 'opus': return 2;
        case 'mp3': return 1;
        default: return 0;
    }
};

const languageCode = (language?: string): string => {
    const code = language?.trim().toLowerCase().split(/[-_]/)[0] ?? '';
    if (!code) return '';
    try {
        return new Intl.Locale(code).language ?? '';
    } catch {
        return code;
    }
};

export function pickBestAudioTrack<T extends AudioTrackChoice>(tracks: T[] | undefined, caps: DeviceCapabilities): T | undefined {
    if (!tracks?.length) return undefined;
    const primary = tracks.find(track => track.isDefault) ?? tracks[0];
    const mainTracks = tracks.filter(track => !track.title?.toLowerCase().includes('commentary'));
    let pool = mainTracks.length ? mainTracks : tracks;
    const primaryLanguage = languageCode(primary.language);
    if (primaryLanguage) {
        const sameLanguage = pool.filter(track => languageCode(track.language) === primaryLanguage);
        if (sameLanguage.length) pool = sameLanguage;
    }
    const outputChannels = caps.maxAudioChannels > 0 ? caps.maxAudioChannels : 2;
    const penalty = (track: T): number => {
        const channels = track.channels && track.channels > 0 ? track.channels : 2;
        return (isAudioDirectPlayable(track, caps) ? 0 : 1000)
            + Math.abs(outputChannels - channels) * 10
            - codecQuality(track.codec) * 5
            - (track.isDefault ? 2 : 0);
    };
    return [...pool].sort((a, b) => penalty(a) - penalty(b))[0];
}
