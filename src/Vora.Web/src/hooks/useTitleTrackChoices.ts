import { useCallback, useEffect, useMemo, useState } from 'react';
import type { MediaItem, MediaPart } from '../api/Media/mediaService';
import { streamingService, type PlaybackChoiceKind, type RememberChoiceChange, type RememberedChoice } from '../api/Streaming/streamingService';
import { pickBestAudioTrack } from '../utils/audioTrackSelection';
import type { DeviceCapabilities } from '../utils/hardwareScanner';
import { isVideoDirectPlayable, parseResolutionHeight } from '../utils/playbackDecision';
import { isNoSubtitle } from '../utils/subtitleKind';
import { useServerPlaybackChoice, type PlaybackChoice } from './useServerPlaybackChoice';

export interface TrackSelection {
    videoId: string;
    audioId: string;
    subtitleId: string;
}

const NothingSelected: TrackSelection = { videoId: '', audioId: '', subtitleId: 'none' };
export const NothingRemembered: RememberedChoice = { remembersVersion: false, remembersAudio: false, subtitle: 'Automatic' };

const defaultVideoOf = (part: MediaPart) => part.videoTracks?.find(track => track.isDefault) ?? part.videoTracks?.[0];
const flaggedSubtitleOf = (part: MediaPart) => part.subtitleTracks?.find(track => track.isForced || track.isDefault)?.id ?? 'none';

// Shown at once while the server is asked: the largest version within the
// device's resolution (or the largest if none fits), its video track that
// plays directly, the audio track that plays best, and the forced or default
// subtitle.
export function localTrackGuess(item: MediaItem, caps: DeviceCapabilities): TrackSelection {
    const parts = item.mediaParts ?? [];
    if (parts.length === 0) return NothingSelected;

    const height = (part: MediaPart) => parseResolutionHeight(part.resolution);
    const fitting = parts.filter(part => height(part) === 0 || height(part) <= caps.requestedMaxResolution);
    const part = (fitting.length ? fitting : parts).reduce((best, candidate) => height(candidate) > height(best) ? candidate : best);

    const video = part.videoTracks?.find(track => isVideoDirectPlayable(track, part, caps)) ?? defaultVideoOf(part);
    return {
        videoId: video?.id ?? '',
        audioId: pickBestAudioTrack(part.audioTracks, caps)?.id ?? '',
        subtitleId: flaggedSubtitleOf(part),
    };
}

// The version and tracks a title page shows, what this profile has picked for
// the title on this device, and the picks that change them. The server decides
// what plays and applies what was picked, so a pick is saved first and the
// server asked again. If it can't be saved, it still applies to this play.
export function useTitleTrackChoices(item: MediaItem | null, caps: DeviceCapabilities, serverId?: string) {
    const serverChoice = useServerPlaybackChoice(serverId);
    const [selection, setSelection] = useState<TrackSelection>(NothingSelected);
    const [audioPickedHere, setAudioPickedHere] = useState(false);
    const [remembered, setRemembered] = useState<RememberedChoice>(NothingRemembered);
    const [pickReplaced, setPickReplaced] = useState(false);

    const show = useCallback((media: MediaItem, choice: PlaybackChoice | null) => {
        if (!choice || !media.mediaParts?.some(part => part.id === choice.mediaPartId)) return;
        setSelection({ videoId: choice.videoTrackId, audioId: choice.audioTrackId, subtitleId: choice.subtitleTrackId ?? 'none' });
        setAudioPickedHere(false);
        setPickReplaced(choice.pickReplaced);
    }, []);

    useEffect(() => {
        if (!item?.mediaParts?.length) return;
        let replaced = false;
        const guess = localTrackGuess(item, caps);
        const showGuessThenAsk = setTimeout(() => {
            setSelection(guess);
            setAudioPickedHere(false);
            setPickReplaced(false);
            serverChoice.ask(item.id).then(choice => { if (!replaced) show(item, choice); });
        }, 0);
        streamingService.getRememberedChoice(item.id, serverId)
            .then(choice => { if (!replaced) setRemembered(choice); })
            .catch(() => { if (!replaced) setRemembered(NothingRemembered); });
        return () => {
            replaced = true;
            clearTimeout(showGuessThenAsk);
        };
    }, [item, caps, serverId, serverChoice, show]);

    const remember = useCallback(async (change: RememberChoiceChange, pickForThisPlay: () => void) => {
        if (!item) return;
        serverChoice.forget();
        try {
            setRemembered(await streamingService.rememberChoice(item.id, change, serverId));
        } catch {
            pickForThisPlay();
            return;
        }
        show(item, await serverChoice.ask(item.id));
    }, [item, serverId, serverChoice, show]);

    const pickVersion = useCallback((partId: string) => {
        const part = item?.mediaParts?.find(candidate => candidate.id === partId);
        if (!part) return Promise.resolve();
        return remember({ mediaPartId: partId }, () => {
            setSelection(current => ({
                videoId: defaultVideoOf(part)?.id ?? current.videoId,
                audioId: pickBestAudioTrack(part.audioTracks, caps)?.id ?? '',
                subtitleId: isNoSubtitle(current.subtitleId) || part.subtitleTracks?.some(track => track.id === current.subtitleId)
                    ? current.subtitleId
                    : flaggedSubtitleOf(part),
            }));
            setAudioPickedHere(false);
        });
    }, [item, caps, remember]);

    const pickAudio = useCallback((audioId: string) => remember({ audioTrackId: audioId }, () => {
        setSelection(current => ({ ...current, audioId }));
        setAudioPickedHere(true);
    }), [remember]);

    const pickSubtitle = useCallback((subtitleId: string) => remember(
        isNoSubtitle(subtitleId) ? { subtitlesOff: true } : { subtitleTrackId: subtitleId },
        () => setSelection(current => ({ ...current, subtitleId })),
    ), [remember]);

    const chooseAutomatically = useCallback((kind: PlaybackChoiceKind) => remember({ forget: [kind] }, () => undefined), [remember]);

    return useMemo(() => ({
        ...selection, audioPickedHere, remembered, pickReplaced,
        pickVersion, pickAudio, pickSubtitle, chooseAutomatically,
    }), [selection, audioPickedHere, remembered, pickReplaced, pickVersion, pickAudio, pickSubtitle, chooseAutomatically]);
}
