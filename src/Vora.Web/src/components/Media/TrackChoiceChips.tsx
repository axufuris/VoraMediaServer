import type { MediaItem, MediaPart } from '../../api/Media/mediaService';
import type { PlaybackChoiceKind, RememberedChoice } from '../../api/Streaming/streamingService';
import { HeroChoiceChip } from '../Client/Primitives/DetailHero';
import { menuChoice, menuSeparator, type MenuEntry } from '../Client/Primitives/menuEntries';
import type { DeviceCapabilities } from '../../utils/hardwareScanner';
import { audioChipLabel, pickChipAudioTrack, resolutionChipLabel, subtitleChipLabel } from '../../utils/heroQualityChips';
import { isAudioDirectPlayable, isVideoDirectPlayable, parseResolutionHeight } from '../../utils/playbackDecision';
import { audioLayout, languageName } from '../../utils/trackLabels';

interface Option {
    value: string;
    label: string;
    detail?: string;
}

interface TrackChoiceChipsProps {
    item: MediaItem;
    activePart?: MediaPart;
    videoId: string;
    audioId: string;
    subtitleId: string;
    remembered: RememberedChoice;
    pickReplaced: boolean;
    caps: DeviceCapabilities;
    onPickVersion: (partId: string) => void;
    onPickAudio: (audioId: string) => void;
    onPickSubtitle: (subtitleId: string) => void;
    onChooseAutomatically: (kind: PlaybackChoiceKind) => void;
}

const joined = (parts: (string | null | undefined | false)[], separator = ' · ') => parts.filter(Boolean).join(separator);
const displayResolution = (part: MediaPart, index: number) => part.resolution === '2160p' ? '4K' : part.resolution || `Version ${index + 1}`;

function versionOptions(item: MediaItem, caps: DeviceCapabilities): Option[] {
    return [...(item.mediaParts ?? [])]
        .sort((a, b) => parseResolutionHeight(b.resolution) - parseResolutionHeight(a.resolution) || (b.bitrateKbps ?? 0) - (a.bitrateKbps ?? 0))
        .map((part, index) => {
            const video = part.videoTracks?.find(track => track.isDefault) ?? part.videoTracks?.[0];
            return {
                value: part.id,
                label: joined([part.edition, displayResolution(part, index), video?.codec?.toUpperCase()]),
                detail: joined([
                    video?.hdrType && video.hdrType !== 'SDR' ? video.hdrType : null,
                    part.bitrateKbps ? `${Math.round(part.bitrateKbps / 1000)} Mbps` : null,
                    video && isVideoDirectPlayable(video, part, caps) ? 'Direct Play' : 'Transcode',
                ]),
            };
        });
}

function audioOptions(part: MediaPart | undefined, caps: DeviceCapabilities): Option[] {
    return [...(part?.audioTracks ?? [])]
        .sort((a, b) => (b.channels ?? 0) - (a.channels ?? 0))
        .map(track => ({
            value: track.id,
            label: joined([languageName(track.language), joined([track.codec?.toUpperCase(), audioLayout(track.channels)], ' ')]),
            detail: joined([track.title, track.isDefault ? 'Default' : null, isAudioDirectPlayable(track, caps) ? 'Direct Play' : 'Transcode']),
        }));
}

function subtitleOptions(part: MediaPart | undefined): Option[] {
    return [
        { value: 'none', label: 'Off' },
        ...(part?.subtitleTracks ?? []).map(track => ({
            value: track.id,
            label: joined([languageName(track.language), track.isForced ? 'Forced' : null, track.isHearingImpaired ? 'SDH' : null], ' '),
            detail: joined([track.title, track.codec?.toUpperCase(), track.isDefault ? 'Default' : null]),
        })),
    ];
}

function choiceEntries(kind: PlaybackChoiceKind, options: Option[], current: string, isRemembered: boolean,
    onPick: (value: string) => void, onChooseAutomatically: (kind: PlaybackChoiceKind) => void): MenuEntry[] {
    if (options.length < 2) return [];
    const playing = options.find(option => option.value === current)?.label;
    return [
        menuChoice(`${kind}-automatic`, 'Automatic', !isRemembered, () => onChooseAutomatically(kind), playing ? `Picks what plays best here · now ${playing}` : 'Picks what plays best here'),
        menuSeparator(`${kind}-picks`),
        ...options.map(option => menuChoice(option.value, option.label, isRemembered && option.value === current, () => onPick(option.value), option.detail || undefined)),
    ];
}

// The resolution, audio and subtitle chips of a title page, each a menu of what
// this title offers. A pick is remembered for the title on this device;
// Automatic hands the choice back to the server.
export default function TrackChoiceChips(props: TrackChoiceChipsProps) {
    const { item, activePart, videoId, audioId, subtitleId, remembered, pickReplaced, caps } = props;
    const resolution = resolutionChipLabel(activePart?.resolution);
    const audio = audioChipLabel(pickChipAudioTrack(activePart?.audioTracks, audioId));
    const subtitles = subtitleChipLabel(activePart?.subtitleTracks, subtitleId);
    if (!resolution && !audio && !subtitles) return null;

    const activeVersion = activePart?.id ?? item.mediaParts?.find(part => part.videoTracks?.some(track => track.id === videoId))?.id ?? '';

    return (
        <>
            {resolution && (
                <HeroChoiceChip tone="accent" label={resolution} title="Version"
                    entries={choiceEntries('Version', versionOptions(item, caps), activeVersion, remembered.remembersVersion, props.onPickVersion, props.onChooseAutomatically)} />
            )}
            {audio && (
                <HeroChoiceChip label={audio} title="Audio"
                    entries={choiceEntries('Audio', audioOptions(activePart, caps), audioId, remembered.remembersAudio, props.onPickAudio, props.onChooseAutomatically)} />
            )}
            {subtitles && (
                <HeroChoiceChip label={subtitles} title="Subtitles"
                    entries={choiceEntries('Subtitles', subtitleOptions(activePart), subtitleId, remembered.subtitle !== 'Automatic', props.onPickSubtitle, props.onChooseAutomatically)} />
            )}
            {pickReplaced && (
                <span className="basis-full text-xs lg:text-right" style={{ color: 'var(--vora-text-muted)' }}>
                    Your pick would need converting on this device, so the closest one that plays as it is is used.
                </span>
            )}
        </>
    );
}
