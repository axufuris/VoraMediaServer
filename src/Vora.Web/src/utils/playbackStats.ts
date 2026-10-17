import type { PlaybackAudioStats, PlaybackSubtitleStats, PlaybackVideoStats } from '../api/Streaming/streamingService';
import { formatCodec } from './mediaInfo';
import { audioLayout, knownLanguageName } from './trackLabels';

// The words Playback info uses for what the server decided. A strategy the
// server sends that isn't listed here is shown as it is.
const METHODS: Record<string, string> = {
    DirectPlay: 'Direct Play',
    Remux: 'Direct Stream',
    Transcode: 'Transcode',
};

const STRATEGIES: Record<string, string> = {
    DirectPlay: 'Direct',
    DirectStream: 'Direct',
    Copy: 'Copy',
    Transcode: 'Transcode',
    BurnIn: 'Burned in',
    None: 'Off',
};

export const methodLabel = (method: string): string => METHODS[method] ?? method;

export const strategyLabel = (strategy: string): string => STRATEGIES[strategy] ?? strategy;

export const isConverted = (strategy?: string | null): boolean => strategy === 'Transcode';

export function rateText(kbps?: number | null): string | null {
    if (kbps == null || !Number.isFinite(kbps) || kbps <= 0) return null;
    return kbps >= 1000 ? `${(kbps / 1000).toFixed(1)} Mbps` : `${Math.round(kbps)} kbps`;
}

// A picture's class by its long and short sides, so a 3840x1600 film is 4K
// and a 1080x1920 phone video is 1080p.
export function pictureClass(width?: number | null, height?: number | null): string | null {
    if (!width || !height) return null;
    const long = Math.max(width, height);
    const short = Math.min(width, height);
    if (long >= 3200 || short >= 1800) return '4K';
    if (long >= 2200 || short >= 1300) return '1440p';
    if (long >= 1600 || short >= 900) return '1080p';
    if (long >= 1100 || short >= 650) return '720p';
    return `${short}p`;
}

export const sizeText = (width?: number | null, height?: number | null): string | null =>
    width && height ? `${width}×${height}` : null;

export const frameRateText = (fps?: number | null): string | null =>
    fps && fps > 0 ? `${Number(fps.toFixed(3))} fps` : null;

export const sampleRateText = (hz?: number | null): string | null =>
    hz && hz > 0 ? `${Number((hz / 1000).toFixed(1))} kHz` : null;

export const speedText = (speed?: number | null): string | null =>
    speed != null && Number.isFinite(speed) && speed > 0 ? `${speed.toFixed(1)}×` : null;

const dynamicRange = (hdr?: string | null): string | null => (hdr && hdr.toUpperCase() !== 'SDR' ? hdr : null);

const joined = (parts: (string | null | undefined)[], separator = ' · '): string =>
    parts.filter((part): part is string => !!part).join(separator);

// "4K HDR10 HEVC"
export function videoSourceTitle(video: PlaybackVideoStats): string {
    return joined([pictureClass(video.width, video.height), dynamicRange(video.hdrType), formatCodec(video.codec)], ' ');
}

// "Main 10 · Level 5.1 · 10-bit · 14.0 Mbps · 23.976 fps"
export function videoSourceDetail(video: PlaybackVideoStats): string {
    return joined([
        video.profile,
        video.level ? `Level ${video.level}` : null,
        video.bitDepth ? `${video.bitDepth}-bit` : null,
        rateText(video.bitrateKbps),
        frameRateText(video.frameRate),
    ]);
}

// "Transcode to H264 · 1920×800 SDR · 6.8 Mbps (peaks 8.0 Mbps)", or "Direct".
export function videoOutput(video: PlaybackVideoStats): string {
    if (!isConverted(video.strategy)) return strategyLabel(video.strategy);
    const peak = video.outputPeakKbps && video.outputPeakKbps !== video.outputBitrateKbps ? rateText(video.outputPeakKbps) : null;
    const rate = rateText(video.outputBitrateKbps);
    return joined([
        `Transcode to ${formatCodec(video.outputCodec) ?? 'video'}`,
        joined([sizeText(video.outputWidth, video.outputHeight), video.outputHdrType], ' ') || null,
        rate && peak ? `${rate} (peaks ${peak})` : rate,
    ]);
}

// "English · DTS-HD MA 5.1 (Default)"
export function audioSourceTitle(audio: PlaybackAudioStats): string {
    const format = joined([audio.profile ?? formatCodec(audio.codec), audioLayout(audio.channels ?? undefined) || null], ' ');
    return `${joined([knownLanguageName(audio.language ?? undefined), format || null])}${audio.isDefault ? ' (Default)' : ''}`;
}

// "48 kHz · 5.1(side) · 3.8 Mbps"
export function audioSourceDetail(audio: PlaybackAudioStats): string {
    return joined([sampleRateText(audio.sampleRate), audio.channelLayout, rateText(audio.bitrateKbps)]);
}

// "Transcode to AAC 2.0 · 192 kbps", or "Direct".
export function audioOutput(audio: PlaybackAudioStats): string {
    if (!isConverted(audio.strategy)) return strategyLabel(audio.strategy);
    return joined([
        `Transcode to ${joined([formatCodec(audio.outputCodec), audioLayout(audio.outputChannels ?? undefined) || null], ' ') || 'audio'}`,
        rateText(audio.outputBitrateKbps),
    ]);
}

// "English (Forced) · PGS · Burned in"
export function subtitleSummary(subtitle: PlaybackSubtitleStats): string {
    const language = knownLanguageName(subtitle.language ?? undefined) ?? subtitle.title ?? 'Subtitles';
    return joined([
        subtitle.isForced ? `${language} (Forced)` : language,
        subtitleCodecName(subtitle.codec),
        strategyLabel(subtitle.strategy),
    ]);
}

export function subtitleCodecName(codec?: string | null): string | null {
    switch (codec?.toLowerCase()) {
        case 'hdmv_pgs_subtitle':
        case 'pgssub':
            return 'PGS';
        case 'dvd_subtitle':
        case 'dvdsub':
            return 'VobSub';
        case 'subrip':
            return 'SRT';
        default:
            return formatCodec(codec);
    }
}

// The server writes each reason as "Video Transcode: …". Shown under the
// stream, the area before the colon is enough as a lead-in.
export function reasonLines(reasons?: string[]): { area: string | null; text: string }[] {
    return (reasons ?? [])
        .filter(reason => !reason.startsWith('['))
        .map(reason => {
            const colon = reason.indexOf(': ');
            return colon > 0 && colon < 32
                ? { area: reason.slice(0, colon), text: reason.slice(colon + 2) }
                : { area: null, text: reason };
        });
}
