import type { MediaMarker, MediaPart, UpNextItemVM } from '../../api/Media/mediaService';
import type { PlayerChromeState, PlayerOutputInfo, TrackOption } from '../../desktop/desktopBridge';
import { formatCodec } from '../../utils/mediaInfo';
import { methodLabel, rateText } from '../../utils/playbackStats';
import { seasonEpisodeLabel } from '../../utils/seasonLabel';
import { audioLayout, languageName } from '../../utils/trackLabels';
import type { PlaybackController } from './usePlaybackController';
import type { UpNextCountdown } from './useUpNextAutoPlay';

export type ChromeTheme = PlayerChromeState['theme'];

export type ChromeStatus = { status: 'loading' | 'playing' } | { status: 'error'; message: string };

export type ChromeSource = Pick<PlaybackController,
    'currentMedia' | 'mediaDetails' | 'upNextData' | 'visibleMarkers' | 'activeSkipPrompt' | 'showSkipCommercial' | 'isEnding'
    | 'autoPlayNext' | 'isEpisode' | 'playingAudioId' | 'effectiveSubtitleId' | 'activeStreamPart' | 'display'>;

const THEME_TOKENS: Record<keyof ChromeTheme, string> = {
    accent: '--vora-accent-500',
    surface: '--vora-bg-raised',
    text: '--vora-text-primary',
    textMuted: '--vora-text-muted',
};

function chromeMarkers(markers: MediaMarker[]): PlayerChromeState['markers'] {
    return markers.map(marker => ({ kind: marker.type, start: marker.startSeconds, end: marker.endSeconds }));
}

function hexChannel(value: string): string {
    return Math.max(0, Math.min(255, Math.round(Number(value)))).toString(16).padStart(2, '0');
}

export function cssColorToHex(color: string): string {
    const match = color.trim().match(/^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)/i);
    if (match) return `#${hexChannel(match[1])}${hexChannel(match[2])}${hexChannel(match[3])}`;
    return /^#[0-9a-f]{6}$/i.test(color.trim()) ? color.trim().toLowerCase() : '';
}

export function readChromeTheme(element: HTMLElement): ChromeTheme {
    const read = (token: string): string => {
        const probe = document.createElement('span');
        probe.style.color = `var(${token})`;
        probe.hidden = true;
        element.appendChild(probe);
        const color = cssColorToHex(getComputedStyle(probe).color);
        probe.remove();
        return color;
    };
    return {
        accent: read(THEME_TOKENS.accent),
        surface: read(THEME_TOKENS.surface),
        text: read(THEME_TOKENS.text),
        textMuted: read(THEME_TOKENS.textMuted),
    };
}

function episodeLine(item: UpNextItemVM): string | null {
    return item.type === 'Episode' && item.seasonNumber != null && item.episodeNumber != null
        ? `${seasonEpisodeLabel(item.seasonNumber, item.episodeNumber)} - ${item.tvShowTitle}`
        : null;
}

function versionLabel(part: MediaPart): string {
    const resolution = part.resolution === '2160p' ? '4K' : (part.resolution || 'Unknown');
    const video = part.videoTracks?.[0];
    return [resolution, video?.hdrType, part.edition || part.versionName].filter(Boolean).join(' · ');
}

function audioOptions(controller: ChromeSource): TrackOption[] {
    return (controller.activeStreamPart?.audioTracks ?? []).map(track => ({
        id: track.id,
        label: track.title || languageName(track.language),
        detail: [formatCodec(track.codec), audioLayout(track.channels)].filter(Boolean).join(' ') || null,
        selected: track.id === controller.playingAudioId,
    }));
}

function subtitleOptions(controller: ChromeSource): TrackOption[] {
    return (controller.activeStreamPart?.subtitleTracks ?? []).map(track => ({
        id: track.id,
        label: track.title || languageName(track.language),
        detail: [track.isForced ? 'Forced' : null, track.isHearingImpaired ? 'SDH' : null, formatCodec(track.codec)].filter(Boolean).join(' · ') || null,
        selected: track.id === controller.effectiveSubtitleId,
    }));
}

function infoRows(controller: ChromeSource, output: PlayerOutputInfo | null): PlayerChromeState['info'] {
    const media = controller.currentMedia;
    const { display } = controller;
    const rows: PlayerChromeState['info'] = [];
    const add = (label: string, value: string | null | undefined) => { if (value) rows.push({ label, value }); };
    add('Playback', media?.strategy ? methodLabel(media.strategy) : null);
    add('Video', [display.resolution, formatCodec(display.videoCodec), display.hdr].filter(Boolean).join(' · '));
    add('Audio', [formatCodec(display.audioCodec), display.audioChannels ? audioLayout(display.audioChannels) : null].filter(Boolean).join(' '));
    add('Container', display.container?.toUpperCase());
    add('Bitrate', rateText(media?.bandwidthKbps));
    if (output) {
        add('Output', [output.width && output.height ? `${output.width}×${output.height}` : null, output.outputTransfer].filter(Boolean).join(' · '));
        add('Decoder', output.hardwareDecoder);
    }
    return rows;
}

export interface ChromeStateInput {
    controller: ChromeSource;
    theme: ChromeTheme;
    status: ChromeStatus;
    output: PlayerOutputInfo | null;
    countdown: UpNextCountdown;
}

export function buildChromeState({ controller, theme, status, output, countdown }: ChromeStateInput): PlayerChromeState {
    const media = controller.currentMedia;
    const nextItem = controller.upNextData?.nextItem ?? null;
    const previousItem = controller.upNextData?.previousItem ?? null;
    const parts = controller.mediaDetails?.mediaParts ?? [];
    const selectedVersion = parts.find(part => part.videoTracks?.some(track => track.id === media?.videoTrackId));
    const countingDown = controller.autoPlayNext && !!nextItem;

    const skip = controller.isEnding
        ? null
        : controller.showSkipCommercial
            ? { label: 'Skip commercial' }
            : controller.activeSkipPrompt ? { label: controller.activeSkipPrompt.label } : null;

    return {
        title: media?.title ?? '',
        subtitle: media?.subtitle || null,
        theme,
        status: status.status,
        statusText: status.status === 'error' ? status.message : null,
        markers: chromeMarkers(controller.visibleMarkers),
        skip,
        upNext: controller.isEnding && nextItem
            ? {
                title: nextItem.title,
                subtitle: episodeLine(nextItem),
                countdownSeconds: countingDown ? (countdown.starting ? 0 : Math.ceil(countdown.secondsLeft)) : null,
            }
            : null,
        audio: audioOptions(controller),
        subtitles: subtitleOptions(controller),
        quality: {
            options: parts.flatMap(part => {
                const video = part.videoTracks?.[0];
                return video ? [{ id: video.id, label: versionLabel(part), selected: part === selectedVersion }] : [];
            }),
            currentLabel: [selectedVersion ? versionLabel(selectedVersion) : controller.display.resolution, media?.strategy ? methodLabel(media.strategy) : null].filter(Boolean).join(' · '),
        },
        info: infoRows(controller, output),
        canPlayNext: controller.isEpisode && !!nextItem,
        canPlayPrevious: controller.isEpisode && !!previousItem,
    };
}
