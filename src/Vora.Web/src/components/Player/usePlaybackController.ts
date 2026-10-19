import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { mediaService, type MediaItem, type MediaMarker, type MediaPart, type UpNextItemVM, type UpNextResultVM } from '../../api/Media/mediaService';
import { profileService, type PlaybackPreferencesVM } from '../../api/Users/profileService';
import { streamingService, type RememberChoiceChange } from '../../api/Streaming/streamingService';
import type { TrackRef } from '../../desktop/desktopBridge';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { useFeatureFlags } from '../../hooks/useFeatureFlags';
import { usePlaybackCapabilities } from '../../hooks/usePlaybackCapabilities';
import { useVideoThumbnails } from '../../hooks/useVideoThumbnails';
import { useDialog } from '../../dialogs';
import { isImageSubtitleCodec, isNoSubtitle, NoSubtitle } from '../../utils/subtitleKind';
import { seasonEpisodeLabel } from '../../utils/seasonLabel';
import { pickBestAudioTrack } from '../../utils/audioTrackSelection';
import { EndStretchSeconds, usePlaybackFinished } from './usePlaybackFinished';
import { useUpNextEnding } from './useUpNextEnding';
import type { PlaybackEngine } from './engine/PlaybackEngine';

type VideoTrackType = NonNullable<MediaPart['videoTracks']>[number];
type AudioTrackType = NonNullable<MediaPart['audioTracks']>[number];
type SubtitleTrackType = NonNullable<MediaPart['subtitleTracks']>[number];

export interface SkipPrompt {
    label: string;
    target: number;
    kind: 'intro' | 'credits-to-scene' | 'credits-to-end';
}

export interface StreamPicks {
    video?: string;
    audio?: string;
    subtitle?: string;
}

export interface StreamSelectOptions {
    onStreamRestart?: () => void;
}

export function usePlaybackController(engine: PlaybackEngine) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const { currentMedia, isMinimized, sessionId, seek, changeStreams, videoRef, playMedia } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const flags = useFeatureFlags();

    const [mediaDetails, setMediaDetails] = useState<MediaItem | null>(null);

    const [selVideo, setSelVideo] = useState('');
    const [selAudio, setSelAudio] = useState('');
    const [selSub, setSelSub] = useState('');

    const [switchedAudio, setSwitchedAudio] = useState<{ sessionId: string; trackId: string } | null>(null);
    const playingAudioId = switchedAudio && switchedAudio.sessionId === currentMedia?.sessionId
        ? switchedAudio.trackId
        : currentMedia?.audioTrackId;

    // The text subtitle currently sideloaded onto the <video>, and the single
    // <track> element carrying it. Exactly one is ever attached: switching
    // subtitles replaces it rather than stacking a second cue source.
    const [textSubtitleId, setTextSubtitleId] = useState<string | null>(null);
    const [isLoadingSubtitle, setIsLoadingSubtitle] = useState(false);
    // What the viewer wants showing, kept across the stream restarts that a
    // video/audio change causes: the sidecar URL is per session, so the track
    // has to be re-attached against the new one.
    const desiredTextSubRef = useRef<string | null>(null);

    const activeStreamPart = mediaDetails?.mediaParts?.find((p: MediaPart) =>
        p.videoTracks?.some((vt: VideoTrackType) => vt.id === currentMedia?.videoTrackId)
    );

    const subtitleTrackRef = (subtitleTrackId: string): TrackRef | null | undefined => {
        if (isNoSubtitle(subtitleTrackId)) return null;
        const track = activeStreamPart?.subtitleTracks?.find((st: SubtitleTrackType) => st.id === subtitleTrackId);
        if (!track || track.isExternal || track.embeddedPosition === undefined || track.embeddedPosition === null) return undefined;
        return { position: track.embeddedPosition, codec: track.codec ?? null, language: track.language ?? null };
    };

    const audioTrackRef = (audioTrackId: string): TrackRef | undefined => {
        const track = activeStreamPart?.audioTracks?.find((at: AudioTrackType) => at.id === audioTrackId);
        if (!track || track.embeddedPosition === undefined) return undefined;
        return { position: track.embeddedPosition, codec: track.codec ?? null, language: track.language ?? null };
    };

    const sideloadSubtitle = async (subtitleTrackId: string | null) => {
        if (!engine.canShowTextSubtitles()) return;

        engine.hideTextSubtitle();

        if (!subtitleTrackId || isNoSubtitle(subtitleTrackId) || !sessionId) {
            setTextSubtitleId(null);
            return;
        }

        const track = activeStreamPart?.subtitleTracks?.find((st: SubtitleTrackType) => st.id === subtitleTrackId);
        setIsLoadingSubtitle(true);
        try {
            const result = await engine.showTextSubtitle(
                { sessionId, trackId: subtitleTrackId, serverId, title: track?.title || track?.language || 'Subtitles', track: subtitleTrackRef(subtitleTrackId) ?? null },
                () => desiredTextSubRef.current === subtitleTrackId,
            );
            if (result === 'unavailable') setTextSubtitleId(null);
            else if (result === 'shown') setTextSubtitleId(subtitleTrackId);
        } finally {
            setIsLoadingSubtitle(false);
        }
    };

    const sideloadSubtitleEvent = useEffectEvent((subtitleTrackId: string | null) => sideloadSubtitle(subtitleTrackId));

    const caps = usePlaybackCapabilities();

    const [upNextData, setUpNextData] = useState<UpNextResultVM | null>(null);

    const [hiddenCommercialId, setHiddenCommercialId] = useState<string | null>(null);

    const activeCommercial = currentMedia?.commercialMarkers?.find(
        m => currentTime >= m.start && currentTime < m.end
    );

    const activeCommId = activeCommercial ? `${activeCommercial.start}-${activeCommercial.end}` : null;

    const showSkipCommercial = activeCommId !== null && activeCommId !== hiddenCommercialId;

    useEffect(() => {
        if (showSkipCommercial && activeCommId) {
            const timer = setTimeout(() => {
                setHiddenCommercialId(activeCommId);
            }, 20000);

            return () => clearTimeout(timer);
        }
    }, [showSkipCommercial, activeCommId]);

    const skipCommercial = () => {
        if (activeCommercial) seek(activeCommercial.end);
        if (activeCommId) setHiddenCommercialId(activeCommId);
    };

    const [markers, setMarkers] = useState<MediaMarker[]>([]);
    const [playbackPrefs, setPlaybackPrefs] = useState<PlaybackPreferencesVM>({
        autoSkipIntro: false,
        autoSkipCredits: false,
        autoPlayNext: true,
        minimumCreditsSceneSeconds: 15
    });
    const [autoPlayCancelledFor, setAutoPlayCancelledFor] = useState<string | null>(null);
    const { finished, markFinished, clearFinished } = usePlaybackFinished(videoRef, currentMedia?.sessionId ?? currentMedia?.id);
    const finishedRef = useRef({ finished, markFinished, clearFinished });
    useEffect(() => {
        finishedRef.current = { finished, markFinished, clearFinished };
    }, [finished, markFinished, clearFinished]);

    useEffect(() => engine.on(event => {
        if (event.type === 'ended') finishedRef.current.markFinished();
        else if (event.type === 'time' && finishedRef.current.finished && event.duration - event.position > EndStretchSeconds) finishedRef.current.clearFinished();
    }), [engine]);

    const setMarkersRef = useRef(setMarkers);
    useEffect(() => {
        setMarkersRef.current = setMarkers;
    }, []);

    useEffect(() => {
        const mediaId = currentMedia?.id;
        const apply = setMarkersRef.current;
        if (!mediaId) { apply([]); return; }
        if (currentMedia?.playbackContextType === 'LiveTv' || currentMedia?.playbackContextType === 'Dvr') {
            apply([]);
            return;
        }
        if (currentMedia?.skipMarkers && currentMedia.skipMarkers.length > 0) {
            apply(currentMedia.skipMarkers);
            return;
        }
        mediaService.getMarkers(mediaId, serverId)
            .then(apply)
            .catch(err => { console.error('Failed to load markers', err); apply([]); });
    }, [currentMedia?.id, currentMedia?.skipMarkers, currentMedia?.playbackContextType, serverId]);

    useEffect(() => {
        profileService.getMyPlaybackPreferences(serverId)
            .then(setPlaybackPrefs)
            .catch(err => console.error('Failed to load playback preferences', err));
    }, [serverId]);

    useSignalREvent<string>('MediaAnalysisUpdated', (updatedId) => {
        if (!currentMedia?.id || updatedId !== currentMedia.id) return;
        mediaService.getMarkers(currentMedia.id, serverId)
            .then(setMarkers)
            .catch(err => console.error('Failed to refresh markers', err));
    });

    // Next-episode previews are always detected and stored; the per-library
    // "preview detection" toggle only controls whether the player surfaces them.
    // When it's off, hide Preview markers so no band or "Skip to Preview" button
    // shows — instant, no re-analyze. The credits marker still covers that stretch.
    const visibleMarkers = useMemo(
        () => (mediaDetails?.enablePreviewDetection ? markers : markers.filter(m => m.type !== 'Preview')),
        [markers, mediaDetails?.enablePreviewDetection]
    );

    const activeSkipPrompt = useMemo<SkipPrompt | null>(() => {
        if (!visibleMarkers.length || !duration || !isFinite(duration)) return null;

        const introOrRecap = visibleMarkers.find(m =>
            (m.type === 'Intro' || m.type === 'Recap') &&
            currentTime >= m.startSeconds && currentTime < m.endSeconds
        );
        if (introOrRecap) {
            return {
                label: introOrRecap.type === 'Recap' ? 'Skip Recap' : 'Skip Intro',
                target: introOrRecap.endSeconds,
                kind: 'intro'
            };
        }

        const inScene = visibleMarkers.some(m =>
            (m.type === 'CreditsScene' || m.type === 'Preview') &&
            currentTime >= m.startSeconds && currentTime < m.endSeconds
        );
        if (inScene) return null;

        const credits = visibleMarkers.find(m =>
            m.type === 'Credits' && currentTime >= m.startSeconds && currentTime < m.endSeconds
        );
        if (credits) {
            const nextScene = visibleMarkers
                .filter(m => (m.type === 'CreditsScene' || m.type === 'Preview') && m.startSeconds > currentTime)
                .sort((a, b) => a.startSeconds - b.startSeconds)[0];

            const minDelta = playbackPrefs.minimumCreditsSceneSeconds;
            if (nextScene && (nextScene.startSeconds - currentTime) >= minDelta) {
                return {
                    label: nextScene.type === 'Preview' ? 'Skip to Preview' : 'Skip to Scene',
                    target: nextScene.startSeconds,
                    kind: 'credits-to-scene'
                };
            }
            return {
                label: 'Skip Credits',
                target: Math.max(currentTime, duration - 2),
                kind: 'credits-to-end'
            };
        }

        return null;
    }, [visibleMarkers, currentTime, duration, playbackPrefs.minimumCreditsSceneSeconds]);

    const autoSkippedRef = useRef<string | null>(null);
    useEffect(() => {
        if (!activeSkipPrompt) {
            autoSkippedRef.current = null;
            return;
        }
        const key = `${activeSkipPrompt.kind}:${activeSkipPrompt.target.toFixed(2)}`;
        if (autoSkippedRef.current === key) return;

        const shouldAutoSkip =
            (activeSkipPrompt.kind === 'intro' && playbackPrefs.autoSkipIntro) ||
            (activeSkipPrompt.kind === 'credits-to-end' && playbackPrefs.autoSkipCredits);

        if (shouldAutoSkip) {
            autoSkippedRef.current = key;
            seek(activeSkipPrompt.target);
        }
    }, [activeSkipPrompt, playbackPrefs.autoSkipIntro, playbackPrefs.autoSkipCredits, seek]);

    const skipToMarker = () => {
        if (activeSkipPrompt) seek(activeSkipPrompt.target);
    };

    useEffect(() => {
        if (currentMedia?.id) {
            if (currentMedia.playbackContextType !== 'LiveTv' && currentMedia.playbackContextType !== 'Dvr' && !currentMedia.isExtra) {
                mediaService.getMediaItem(currentMedia.id, serverId).then(setMediaDetails).catch(console.error);
                mediaService.getUpNext(currentMedia.id, currentMedia.playbackContextType, currentMedia.playbackContextId, serverId)
                    .then(setUpNextData)
                    .catch(console.error);
            } else {
                window.setTimeout(() => setMediaDetails(null), 0);
            }

            window.setTimeout(() => {
                setSelVideo(currentMedia.videoTrackId || '');
                setSelAudio(currentMedia.audioTrackId || '');
                const committedText = currentMedia.textSubtitleTrackId ?? null;
                desiredTextSubRef.current = committedText;
                setSelSub(committedText || currentMedia.subtitleTrackId || NoSubtitle);
            }, 0);
        }
    }, [currentMedia?.id, currentMedia?.videoTrackId, currentMedia?.audioTrackId, currentMedia?.subtitleTrackId, currentMedia?.textSubtitleTrackId, currentMedia?.isExtra, currentMedia?.playbackContextType, currentMedia?.playbackContextId, serverId]);

    const streamSource = useEffectEvent(() => ({
        title: currentMedia?.title ?? '',
        startPosition: currentMedia?.startPosition ?? 0,
        audioTrack: currentMedia?.audioTrackPosition !== undefined
            ? { position: currentMedia.audioTrackPosition, codec: currentMedia.audioCodec ?? null, language: null }
            : null,
        subtitleTrack: currentMedia?.subtitleTrackPosition !== undefined && currentMedia.subtitleTrackPosition !== null
            ? { position: currentMedia.subtitleTrackPosition, codec: null, language: null }
            : null,
    }));

    useEffect(() => {
        const streamUrl = currentMedia?.streamUrl;
        if (!streamUrl) return;

        void engine.load({
            url: streamUrl,
            container: currentMedia?.container,
            strategy: currentMedia?.strategy,
            isFinished: () => finishedRef.current.finished,
            ...streamSource(),
        });

        return () => {
            engine.unload();
        };
    }, [currentMedia?.streamUrl, currentMedia?.container, currentMedia?.strategy, engine]);

    useEffect(() => {
        if (!sessionId) return;
        const desired = desiredTextSubRef.current;
        if (!desired) return;

        void sideloadSubtitleEvent(desired);
    }, [sessionId, serverId]);

    // The blob URLs belong to the session that produced them; a session that has
    // gone away can never be asked for them again.
    useEffect(() => {
        if (!sessionId) return;
        return () => {
            engine.hideTextSubtitle();
            streamingService.releaseSubtitleUrls(sessionId);
        };
    }, [sessionId, engine]);

    const refreshUpNext = useCallback(() => {
        if (!currentMedia) return;
        mediaService.getUpNext(currentMedia.id, currentMedia.playbackContextType, currentMedia.playbackContextId, serverId)
            .then(setUpNextData)
            .catch(console.error);
    }, [currentMedia, serverId]);

    const isEnding = useUpNextEnding({
        mediaId: currentMedia?.id,
        eligible: !!currentMedia
            && !isMinimized
            && !currentMedia.isExtra
            && currentMedia.playbackContextType !== 'Dvr'
            && currentMedia.playbackContextType !== 'LiveTv',
        currentTime,
        duration,
        finished,
        onEnterEnding: refreshUpNext,
    });

    const thumbnailsEligible = currentMedia?.playbackContextType !== 'LiveTv' && currentMedia?.playbackContextType !== 'Dvr';
    // Thumbnails are per media part (different cuts get their own sprite). Resolve
    // the part being streamed from the active video track so scrubbing the 4K/1080p
    // or an alternate cut shows the sprite for the file actually playing.
    const activePartId = useMemo(() => {
        if (!mediaDetails?.mediaParts || !currentMedia?.videoTrackId) return undefined;
        return mediaDetails.mediaParts.find(p => p.videoTracks?.some(t => t.id === currentMedia.videoTrackId))?.id;
    }, [mediaDetails?.mediaParts, currentMedia?.videoTrackId]);
    const thumbnails = useVideoThumbnails(thumbnailsEligible ? currentMedia?.id : undefined, activePartId, serverId);

    const readPlayer = useCallback(() => engine.read(), [engine]);

    const isEpisode = mediaDetails?.type === 'Episode' || /^S\d+\s*E\d+/i.test(currentMedia?.subtitle ?? '');

    const autoPlayNext = playbackPrefs.autoPlayNext !== false && autoPlayCancelledFor !== currentMedia?.id;

    const cancelAutoPlay = () => {
        if (currentMedia) setAutoPlayCancelledFor(currentMedia.id);
    };

    const playNext = async (item: UpNextItemVM) => {
        if (!currentMedia) return;
        const deviceId = localStorage.getItem('device_id') || 'unknown';

        if (sessionId) {
            try {
                await streamingService.pingSession(sessionId, currentTime, duration, true, serverId);
                await streamingService.stopSession(sessionId, serverId);
            } catch (e) { console.error(e); }
        }

        try {
            const sessionInfo = await streamingService.startSession(item.id, deviceId, 0, undefined, undefined, undefined, serverId);

            playMedia({
                id: item.id,
                title: item.title,
                subtitle: item.type === 'Episode' && item.seasonNumber != null && item.episodeNumber != null ? `${seasonEpisodeLabel(item.seasonNumber, item.episodeNumber)} - ${item.tvShowTitle}` : '',
                posterUrl: item.posterUrl,
                backgroundUrl: item.backgroundUrl,
                ...sessionInfo,
                serverId,
                startPosition: 0,
                playbackContextType: currentMedia.playbackContextType,
                playbackContextId: currentMedia.playbackContextId,
            });
        } catch {
            await dialog.alert('Failed to start next item.');
        }
    };

    // A pick made while watching is remembered for the title on this device,
    // like a pick on its page, so the next episode plays the same way.
    const rememberPick = (change: RememberChoiceChange) => {
        if (!currentMedia || currentMedia.isExtra || currentMedia.playbackContextType === 'LiveTv' || currentMedia.playbackContextType === 'Dvr') return;
        if (!change.mediaPartId && !change.audioTrackId && !change.subtitleTrackId && !change.subtitlesOff) return;
        streamingService.rememberChoice(currentMedia.id, change, serverId)
            .catch((error: unknown) => console.error('Could not remember the playback choice', error));
    };

    const subtitlePick = (subtitleId: string): RememberChoiceChange =>
        isNoSubtitle(subtitleId) ? { subtitlesOff: true } : { subtitleTrackId: subtitleId };

    const selectSubtitleLocally = (subtitleTrackId: string): boolean => {
        const subtitleTrack = subtitleTrackRef(subtitleTrackId);
        if (subtitleTrack === undefined || !engine.selectLocalTracks({ subtitleTrack })) return false;
        setTextSubtitleId(subtitleTrack === null ? NoSubtitle : subtitleTrackId);
        return true;
    };

    const applyStreams = async (picks: StreamPicks = {}) => {
        if (!currentMedia) return;

        const pickedVideo = picks.video ?? selVideo;
        const pickedAudio = picks.audio ?? selAudio;
        const pickedSubtitle = picks.subtitle ?? selSub;
        if (picks.video !== undefined) setSelVideo(pickedVideo);
        if (picks.audio !== undefined) setSelAudio(pickedAudio);
        if (picks.subtitle !== undefined) setSelSub(pickedSubtitle);

        const shownSubtitle = currentMedia.subtitleStrategy === 'BurnIn' ? currentMedia.subtitleTrackId : textSubtitleId;
        rememberPick({
            mediaPartId: pickedVideo !== currentMedia.videoTrackId
                ? mediaDetails?.mediaParts?.find((p: MediaPart) => p.videoTracks?.some((vt: VideoTrackType) => vt.id === pickedVideo))?.id
                : undefined,
            audioTrackId: pickedAudio !== playingAudioId ? pickedAudio : undefined,
            ...((isNoSubtitle(pickedSubtitle) ? null : pickedSubtitle) !== (shownSubtitle ?? null) ? subtitlePick(pickedSubtitle) : {}),
        });

        const chosenTrack = mediaDetails?.mediaParts
            ?.flatMap((p: MediaPart) => p.subtitleTracks ?? [])
            .find((st: SubtitleTrackType) => st.id === pickedSubtitle);
        const wantsBurnIn = !!chosenTrack && isImageSubtitleCodec(chosenTrack.codec);
        const videoChanged = pickedVideo !== currentMedia.videoTrackId;
        const audioChanged = pickedAudio !== playingAudioId;
        const audioTrack = !videoChanged && audioChanged ? audioTrackRef(pickedAudio) : undefined;
        const audioSwitchedLocally = !!audioTrack && !!currentMedia.sessionId && engine.selectLocalTracks({ audioTrack });
        if (audioSwitchedLocally && currentMedia.sessionId) setSwitchedAudio({ sessionId: currentMedia.sessionId, trackId: pickedAudio });
        const streamChanged = videoChanged || (audioChanged && !audioSwitchedLocally);
        const hasBurnedInSub = currentMedia.subtitleStrategy === 'BurnIn';

        desiredTextSubRef.current = wantsBurnIn || isNoSubtitle(pickedSubtitle) ? null : pickedSubtitle;

        if (!streamChanged && !hasBurnedInSub && selectSubtitleLocally(pickedSubtitle)) return;

        // Only a video/audio change, or adding or dropping a burned-in subtitle,
        // needs a new stream — the server has to re-encode for those. A text
        // subtitle is cues the client draws, so switching one (or turning
        // subtitles off) leaves the stream alone and the video never reloads.
        if (streamChanged || wantsBurnIn || hasBurnedInSub) {
            engine.hideTextSubtitle();
            setTextSubtitleId(null);
            await changeStreams(pickedVideo, pickedAudio, wantsBurnIn ? pickedSubtitle : NoSubtitle);
            return;
        }

        await sideloadSubtitle(desiredTextSubRef.current);
    };

    const selectSubtitle = async (subtitleTrackId: string, options: StreamSelectOptions = {}) => {
        const chosen = mediaDetails?.mediaParts
            ?.flatMap((p: MediaPart) => p.subtitleTracks ?? [])
            .find((st: SubtitleTrackType) => st.id === subtitleTrackId);

        const wantsBurnIn = !!chosen && isImageSubtitleCodec(chosen.codec);
        const hasBurnedInSub = currentMedia?.subtitleStrategy === 'BurnIn';

        setSelSub(subtitleTrackId);
        desiredTextSubRef.current = wantsBurnIn || isNoSubtitle(subtitleTrackId) ? null : subtitleTrackId;
        rememberPick(subtitlePick(subtitleTrackId));

        if (!hasBurnedInSub && selectSubtitleLocally(subtitleTrackId)) return;

        // Adding a burned-in subtitle, or dropping one that is already burned
        // into the picture, both need a new encode — nothing the client can undo.
        if (wantsBurnIn || hasBurnedInSub) {
            options.onStreamRestart?.();
            engine.hideTextSubtitle();
            setTextSubtitleId(null);
            await changeStreams(selVideo, selAudio, wantsBurnIn ? subtitleTrackId : NoSubtitle);
            return;
        }

        await sideloadSubtitle(desiredTextSubRef.current);
    };

    const selectAudio = (audioTrackId: string) => applyStreams({ audio: audioTrackId });

    const selectVersion = (videoTrackId: string) => {
        const part = mediaDetails?.mediaParts?.find((p: MediaPart) => p.videoTracks?.some((vt: VideoTrackType) => vt.id === videoTrackId));
        return applyStreams({
            video: videoTrackId,
            audio: part?.audioTracks?.length ? pickBestAudioTrack(part.audioTracks, caps)?.id ?? '' : '',
            subtitle: NoSubtitle,
        });
    };

    const restartStream = () => {
        if (!currentMedia) return Promise.resolve();
        const track = activeStreamPart?.subtitleTracks?.find((st: SubtitleTrackType) => st.id === selSub);
        const burnIn = !!track && isImageSubtitleCodec(track.codec);
        return changeStreams(currentMedia.videoTrackId ?? '', playingAudioId ?? '', burnIn ? selSub : NoSubtitle);
    };

    const subtitleDownloaded = async (subtitleTrackId: string) => {
        if (!currentMedia) return;

        try {
            setMediaDetails(await mediaService.getMediaItem(currentMedia.id, serverId));
        } catch (e) {
            console.error('Failed to refresh tracks after downloading a subtitle', e);
        }

        setSelSub(subtitleTrackId);
        desiredTextSubRef.current = subtitleTrackId;
        rememberPick({ subtitleTrackId });
        await sideloadSubtitle(subtitleTrackId);
    };

    const canFindSubtitles = flags.subtitleSearch && !currentMedia?.isExtra;

    const activeVideoTrack = activeStreamPart?.videoTracks?.find((vt: VideoTrackType) => vt.id === currentMedia?.videoTrackId);
    const activeAudioTrack = activeStreamPart?.audioTracks?.find((at: AudioTrackType) => at.id === playingAudioId);
    const effectiveSubtitleId = textSubtitleId ?? currentMedia?.subtitleTrackId;

    // Nothing to open when the file carries no subtitles and no provider could
    // fetch one — the control is hidden rather than shown leading nowhere.
    const hasSubtitleOptions = (activeStreamPart?.subtitleTracks?.length ?? 0) > 0 || canFindSubtitles;
    const activeSubtitleTrack = activeStreamPart?.subtitleTracks?.find((st: SubtitleTrackType) => st.id === effectiveSubtitleId);

    // When a transcode is in play, the player gets WHAT THE SERVER
    // DELIVERS, not the source. The badge has to reflect the output —
    // otherwise it lies about a 4K HDR → 1080p SDR transcode. Prefer
    // the session's outputResolution / outputHdrType (set from the
    // StartStream response) when the video is being transcoded; fall
    // back to source-side fields only when video strategy is
    // DirectPlay / DirectStream (source flows through unchanged).
    const isVideoTranscoded = currentMedia?.videoStrategy === 'Transcode';
    const rawDisplayRes = isVideoTranscoded && currentMedia?.outputResolution
        ? currentMedia.outputResolution
        : (activeStreamPart ? activeStreamPart.resolution : currentMedia?.resolution);
    const displayRes = rawDisplayRes === '2160p' ? '4K' : rawDisplayRes;

    const displayHdr = isVideoTranscoded && currentMedia?.outputHdrType
        ? currentMedia.outputHdrType
        : (activeStreamPart ? activeVideoTrack?.hdrType : currentMedia?.hdrType);

    const displayVideoCodec = currentMedia?.videoStrategy === 'Transcode' ? currentMedia.videoCodec : (activeVideoTrack?.codec || currentMedia?.videoCodec);
    const displayAudioCodec = currentMedia?.audioStrategy === 'Transcode' ? currentMedia.audioCodec : (activeAudioTrack?.codec || currentMedia?.audioCodec);
    const displayAudioChannels = currentMedia?.audioStrategy === 'Transcode' ? currentMedia.targetAudioChannels : (activeAudioTrack?.channels || currentMedia?.audioChannels);

    return {
        currentMedia,
        serverId,
        sessionId,
        mediaDetails,
        upNextData,
        caps,
        isEpisode,
        visibleMarkers,
        activeSkipPrompt,
        skipToMarker,
        showSkipCommercial,
        skipCommercial,
        isEnding,
        finished,
        autoPlayNext,
        cancelAutoPlay,
        playNext,
        selVideo,
        selAudio,
        selSub,
        playingAudioId,
        setSelVideo,
        setSelAudio,
        applyStreams,
        selectSubtitle,
        selectAudio,
        selectVersion,
        restartStream,
        subtitleDownloaded,
        isLoadingSubtitle,
        effectiveSubtitleId,
        canFindSubtitles,
        hasSubtitleOptions,
        activeStreamPart,
        activeSubtitleTrack,
        thumbnails,
        readPlayer,
        display: {
            resolution: displayRes,
            hdr: displayHdr,
            videoCodec: displayVideoCodec,
            audioCodec: displayAudioCodec,
            audioChannels: displayAudioChannels,
            container: currentMedia?.container,
        },
    };
}

export type PlaybackController = ReturnType<typeof usePlaybackController>;
