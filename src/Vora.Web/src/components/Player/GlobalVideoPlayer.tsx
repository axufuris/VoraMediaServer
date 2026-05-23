import { usePlayer } from '../../contexts/usePlayer';
import { useEffect, useState, useRef, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { mediaService, type MediaItem, type MediaPart, type UpNextItemVM, type UpNextResultVM, type MediaMarker } from '../../api/Media/mediaService';
import { profileService, type PlaybackPreferencesVM } from '../../api/Users/profileService';
import { streamingService } from '../../api/Streaming/streamingService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { scanDeviceCapabilities } from '../../utils/hardwareScanner';
import Hls from 'hls.js';
import { useDialog } from '../../dialogs';
import { useAutoHideControls } from './Controls/useAutoHideControls';
import { useFullscreen } from './Controls/useFullscreen';
import { PlayPauseButton, SkipButton, VolumeControl, FullscreenButton, MaximizeButton, CloseButton, EpisodeNavButton } from './Controls/PlayerButtons';
import PlayerSettingsPanel from './Panels/PlayerSettingsPanel';
import PlayerInfoPanel from './Panels/PlayerInfoPanel';
import UpNextOverlay from './Panels/UpNextOverlay';

type VideoTrackType = NonNullable<MediaPart['videoTracks']>[number];
type AudioTrackType = NonNullable<MediaPart['audioTracks']>[number];
type SubtitleTrackType = NonNullable<MediaPart['subtitleTracks']>[number];

const chipStyle: React.CSSProperties = {
    background: 'rgba(20, 20, 28, 0.72)',
    border: '1px solid rgba(255, 255, 255, 0.18)',
    color: '#fafafa',
    backdropFilter: 'blur(8px)',
    WebkitBackdropFilter: 'blur(8px)',
};

const accentChipStyle: React.CSSProperties = {
    background: 'var(--vora-accent-500)',
    border: '1px solid var(--vora-accent-500)',
    color: 'var(--vora-accent-contrast)',
};

const markerBandColors: Record<string, string> = {
    Intro: 'rgba(255, 255, 255, 0.22)',
    Recap: 'rgba(255, 255, 255, 0.22)',
    Credits: 'rgba(120, 170, 255, 0.32)',
    CreditsScene: 'var(--vora-accent-500)',
    Preview: 'var(--vora-accent-500)',
};

function MarkerBands({ markers, duration }: { markers: MediaMarker[]; duration: number }) {
    if (!markers.length || !duration || !isFinite(duration) || duration <= 0) return null;
    return (
        <div className="pointer-events-none absolute inset-0 overflow-hidden rounded-full">
            {markers.map((m, idx) => {
                const start = Math.max(0, Math.min(100, (m.startSeconds / duration) * 100));
                const end = Math.max(0, Math.min(100, (m.endSeconds / duration) * 100));
                const width = Math.max(0.4, end - start);
                const bg = markerBandColors[m.type] ?? 'rgba(255, 255, 255, 0.18)';
                const isScene = m.type === 'CreditsScene' || m.type === 'Preview';
                return (
                    <div
                        key={`${m.type}-${m.order}-${idx}`}
                        className="absolute top-0 h-full"
                        style={{
                            left: `${start}%`,
                            width: `${width}%`,
                            background: bg,
                            opacity: isScene ? 0.85 : 1,
                            mixBlendMode: isScene ? 'normal' : 'screen',
                        }}
                        title={`${m.type} ${Math.round(m.startSeconds)}s – ${Math.round(m.endSeconds)}s`}
                    />
                );
            })}
        </div>
    );
}

export default function GlobalVideoPlayer() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const {
        currentMedia, isPlaying, isMinimized, currentTime, duration, volume, sessionId,
        togglePlayPause, seek, skipForward, skipBackward, setMinimized, closePlayer, setVolume, changeStreams, videoRef, playMedia,
    } = usePlayer();

    const playerContainerRef = useRef<HTMLDivElement>(null);

    const [mediaDetails, setMediaDetails] = useState<MediaItem | null>(null);
    const [showSettings, setShowSettings] = useState(false);
    const [showInfo, setShowInfo] = useState(false);

    const [selVideo, setSelVideo] = useState('');
    const [selAudio, setSelAudio] = useState('');
    const [selSub, setSelSub] = useState('');

    const caps = useMemo(() => scanDeviceCapabilities(), []);

    const [isEnding, setIsEnding] = useState(false);
    const [upNextData, setUpNextData] = useState<UpNextResultVM | null>(null);
    const hasFetchedUpNext = useRef(false);

    const [hiddenCommercialId, setHiddenCommercialId] = useState<string | null>(null);

    const activeCommercial = currentMedia?.commercialMarkers?.find(
        m => currentTime >= m.start && currentTime < m.end
    );

    const activeCommId = activeCommercial ? `${activeCommercial.start}-${activeCommercial.end}` : null;

    const showSkipButton = activeCommId !== null && activeCommId !== hiddenCommercialId;

    useEffect(() => {
        if (showSkipButton && activeCommId) {
            const timer = setTimeout(() => {
                setHiddenCommercialId(activeCommId);
            }, 20000);

            return () => clearTimeout(timer);
        }
    }, [showSkipButton, activeCommId]);

    const [markers, setMarkers] = useState<MediaMarker[]>([]);
    const [playbackPrefs, setPlaybackPrefs] = useState<PlaybackPreferencesVM>({
        autoSkipIntro: false,
        autoSkipCredits: false,
        minimumCreditsSceneSeconds: 15
    });

    useEffect(() => {
        const mediaId = currentMedia?.id;
        if (!mediaId) { setMarkers([]); return; }
        if (currentMedia?.playbackContextType === 'LiveTv' || currentMedia?.playbackContextType === 'Dvr') {
            setMarkers([]);
            return;
        }
        if (currentMedia?.skipMarkers && currentMedia.skipMarkers.length > 0) {
            setMarkers(currentMedia.skipMarkers);
            return;
        }
        mediaService.getMarkers(mediaId, serverId)
            .then(setMarkers)
            .catch(err => { console.error('Failed to load markers', err); setMarkers([]); });
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

    const activeSkipPrompt = useMemo<{ label: string; target: number; kind: 'intro' | 'credits-to-scene' | 'credits-to-end' } | null>(() => {
        if (!markers.length || !duration || !isFinite(duration)) return null;

        const introOrRecap = markers.find(m =>
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

        const inScene = markers.some(m =>
            (m.type === 'CreditsScene' || m.type === 'Preview') &&
            currentTime >= m.startSeconds && currentTime < m.endSeconds
        );
        if (inScene) return null;

        const credits = markers.find(m =>
            m.type === 'Credits' && currentTime >= m.startSeconds && currentTime < m.endSeconds
        );
        if (credits) {
            const nextScene = markers
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
    }, [markers, currentTime, duration, playbackPrefs.minimumCreditsSceneSeconds]);

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

    useEffect(() => {
        if (currentMedia?.id) {
            if (currentMedia.playbackContextType !== 'LiveTv' && currentMedia.playbackContextType !== 'Dvr') {
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
                setSelSub(currentMedia.subtitleTrackId || 'none');
                setIsEnding(false);
            }, 0);

            hasFetchedUpNext.current = true;
        }
    }, [currentMedia?.id, currentMedia?.videoTrackId, currentMedia?.audioTrackId, currentMedia?.subtitleTrackId, currentMedia?.playbackContextType, currentMedia?.playbackContextId, serverId]);

    useEffect(() => {
        const video = videoRef.current;
        if (!video || !currentMedia?.streamUrl) return;

        let hls: Hls | null = null;

        if (currentMedia.container === 'hls' || currentMedia.strategy === 'Transcode' || currentMedia.streamUrl.includes('.m3u8')) {
            if (Hls.isSupported()) {
                hls = new Hls({
                    enableWorker: true,
                    lowLatencyMode: true,
                });
                hls.loadSource(currentMedia.streamUrl);
                hls.attachMedia(video);
                hls.on(Hls.Events.MANIFEST_PARSED, () => {
                    video.play().catch(e => console.error('Auto-play blocked:', e));
                });

                hls.on(Hls.Events.ERROR, (_, data) => {
                    if (data.fatal) {
                        if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                            hls?.startLoad();
                        } else {
                            hls?.destroy();
                        }
                    }
                });
            } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                video.src = currentMedia.streamUrl;
                video.addEventListener('loadedmetadata', () => {
                    video.play().catch(e => console.error('Auto-play blocked:', e));
                });
            }
        } else {
            video.src = currentMedia.streamUrl;
            video.play().catch(e => console.error('Auto-play blocked:', e));
        }

        return () => {
            if (hls) {
                hls.destroy();
            }
            video.removeAttribute('src');
            video.load();
        };
    }, [currentMedia?.streamUrl, currentMedia?.container, currentMedia?.strategy, videoRef]);

    useEffect(() => {
        if (!currentMedia || isMinimized || duration === 0) return;

        if (currentMedia.playbackContextType === 'Dvr' || currentMedia.playbackContextType === 'LiveTv') return;

        const timeRemaining = duration - currentTime;

        if (timeRemaining <= 15 && timeRemaining > 0 && !hasFetchedUpNext.current) {
            hasFetchedUpNext.current = true;
            window.setTimeout(() => setIsEnding(true), 0);
            mediaService.getUpNext(currentMedia.id, currentMedia.playbackContextType, currentMedia.playbackContextId, serverId)
                .then(setUpNextData)
                .catch(console.error);
        }

        if (timeRemaining > 15 && isEnding) {
            hasFetchedUpNext.current = false;
            window.setTimeout(() => setIsEnding(false), 0);
        }
    }, [currentTime, duration, currentMedia, isMinimized, isEnding, serverId]);

    const showControls = useAutoHideControls({
        isMinimized,
        isPlaying,
        keepVisibleWhen: isEnding || showSettings || showInfo,
    });

    const toggleFullScreen = useFullscreen(playerContainerRef);

    if (!currentMedia) return null;

    const isEpisode = mediaDetails?.type === 'Episode' || /^S\d+\s*E\d+/i.test(currentMedia.subtitle ?? '');

    const formatTime = (seconds: number) => {
        if (isNaN(seconds) || !isFinite(seconds)) return '0:00';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = Math.floor(seconds % 60);
        if (h > 0) return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
        return `${m}:${s.toString().padStart(2, '0')}`;
    };

    const handleProgressClick = (e: React.MouseEvent<HTMLDivElement>) => {
        if (!isFinite(duration) || duration <= 0) return;
        const bounds = e.currentTarget.getBoundingClientRect();
        const percent = (e.clientX - bounds.left) / bounds.width;
        seek(percent * duration);
    };

    const handlePlayNext = async (item: UpNextItemVM) => {
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
                subtitle: item.type === 'Episode' ? `S${item.seasonNumber} E${item.episodeNumber} - ${item.tvShowTitle}` : '',
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

    const handleApplyStreams = async () => {
        setShowSettings(false);
        await changeStreams(selVideo, selAudio, selSub);
    };

    const progressPercent = duration > 0 && isFinite(duration) ? Math.min(100, Math.max(0, (currentTime / duration) * 100)) : 0;

    const activeStreamPart = mediaDetails?.mediaParts?.find((p: MediaPart) =>
        p.videoTracks?.some((vt: VideoTrackType) => vt.id === currentMedia.videoTrackId)
    );

    const activeVideoTrack = activeStreamPart?.videoTracks?.find((vt: VideoTrackType) => vt.id === currentMedia.videoTrackId);
    const activeAudioTrack = activeStreamPart?.audioTracks?.find((at: AudioTrackType) => at.id === currentMedia.audioTrackId);
    const activeSubtitleTrack = activeStreamPart?.subtitleTracks?.find((st: SubtitleTrackType) => st.id === currentMedia.subtitleTrackId);

    const displayRes = activeStreamPart ? (activeStreamPart.resolution === '2160p' ? '4K' : activeStreamPart.resolution) : (currentMedia.resolution === '2160p' ? '4K' : currentMedia.resolution);

    const displayHdr = activeStreamPart ? activeVideoTrack?.hdrType : currentMedia.hdrType;

    const displayVideoCodec = currentMedia.videoStrategy === 'Transcode' ? currentMedia.videoCodec : (activeVideoTrack?.codec || currentMedia.videoCodec);
    const displayAudioCodec = currentMedia.audioStrategy === 'Transcode' ? currentMedia.audioCodec : (activeAudioTrack?.codec || currentMedia.audioCodec);
    const displayAudioChannels = currentMedia.audioStrategy === 'Transcode' ? currentMedia.targetAudioChannels : (activeAudioTrack?.channels || currentMedia.audioChannels);

    const displayContainer = currentMedia.container;

    return (
        <div
            ref={playerContainerRef}
            className={`transition-all duration-300 ease-in-out ${isMinimized
                ? 'fixed bottom-0 left-0 right-0 z-[99999] flex h-24 flex-col vora-glass'
                : 'fixed inset-0 z-[99999] bg-black'
                }`}
            style={isMinimized ? { borderTop: '1px solid var(--vora-border-subtle)' } : undefined}
        >

            <video
                key={currentMedia.sessionId}
                ref={videoRef}
                onClick={() => { if (isMinimized) setMinimized(false); }}
                className={`absolute bg-black transition-all duration-700 ease-in-out
                    ${isMinimized ? 'bottom-4 left-6 z-20 h-16 w-16 cursor-pointer rounded-md object-cover shadow-lg hover:ring-2 hover:ring-[var(--vora-accent-500)]' :
                        isEnding ? 'left-20 top-32 z-20 aspect-video w-96 cursor-pointer rounded-xl object-cover shadow-2xl' :
                            'absolute left-0 top-0 z-0 h-[100vh] w-full object-contain'}`}
                style={isEnding && !isMinimized ? { border: '1px solid var(--vora-border-strong)' } : undefined}
            />

            {isEnding && !isMinimized && (
                <UpNextOverlay
                    currentMedia={{ title: currentMedia.title, subtitle: currentMedia.subtitle }}
                    upNextData={upNextData}
                    onPlayNext={handlePlayNext}
                    onClose={closePlayer}
                />
            )}

            {isMinimized && (
                <div className="z-10 flex h-full w-full flex-1 items-center justify-between px-6">
                    <div className="flex w-1/4 items-center gap-4">
                        <div className="h-16 w-16 flex-shrink-0"></div>
                        <div className="flex flex-col overflow-hidden">
                            <span
                                className="cursor-pointer truncate font-semibold hover:underline"
                                onClick={() => setMinimized(false)}
                                style={{ color: 'var(--vora-text-primary)' }}
                            >
                                {currentMedia.title}
                            </span>
                            <span className="truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>{currentMedia.subtitle}</span>
                        </div>
                    </div>

                    <div className="flex max-w-2xl flex-1 flex-col items-center justify-center px-8">
                        <div className="mb-2 flex items-center gap-6">
                            {isEpisode && (
                                <EpisodeNavButton
                                    direction="previous"
                                    size="sm"
                                    disabled={!upNextData?.previousItem}
                                    onClick={() => upNextData?.previousItem && handlePlayNext(upNextData.previousItem)}
                                    title={upNextData?.previousItem ? `Previous: ${upNextData.previousItem.title}` : 'No previous episode'}
                                />
                            )}
                            <SkipButton seconds={10} direction="back" size="sm" onClick={() => skipBackward(10)} />
                            <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} size="sm" />
                            <SkipButton seconds={30} direction="forward" size="sm" onClick={() => skipForward(30)} />
                            {isEpisode && (
                                <EpisodeNavButton
                                    direction="next"
                                    size="sm"
                                    disabled={!upNextData?.nextItem}
                                    onClick={() => upNextData?.nextItem && handlePlayNext(upNextData.nextItem)}
                                    title={upNextData?.nextItem ? `Next: ${upNextData.nextItem.title}` : 'No next episode'}
                                />
                            )}
                        </div>
                        <div className="flex w-full items-center gap-3 text-xs font-medium tabular-nums" style={{ color: 'var(--vora-text-muted)' }}>
                            <span>{formatTime(currentTime)}</span>
                            <div
                                className="group relative h-1.5 flex-1 cursor-pointer rounded-full"
                                onClick={handleProgressClick}
                                style={{ background: 'rgba(255, 255, 255, 0.12)' }}
                            >
                                <MarkerBands markers={markers} duration={duration} />
                                <div className="absolute left-0 top-0 h-full rounded-full" style={{ width: `${progressPercent}%`, background: 'var(--vora-accent-500)' }} />
                                <div
                                    className="absolute top-1/2 -mt-1.5 h-3 w-3 rounded-full opacity-0 shadow group-hover:opacity-100"
                                    style={{ left: `calc(${progressPercent}% - 6px)`, background: '#ffffff' }}
                                />
                            </div>
                            <span>{formatTime(duration)}</span>
                        </div>
                    </div>

                    <div className="flex w-1/4 items-center justify-end gap-4">
                        <MaximizeButton onClick={() => setMinimized(false)} />
                        <CloseButton onClick={closePlayer} />
                    </div>
                </div>
            )}

            {showSkipButton && !isMinimized && !isEnding && (
                <div className="absolute bottom-32 right-12 z-[200] animate-fade-in-up">
                    <button
                        type="button"
                        onClick={(e) => {
                            e.stopPropagation();
                            if (activeCommercial) seek(activeCommercial.end);
                            if (activeCommId) setHiddenCommercialId(activeCommId);
                        }}
                        className="pointer-events-auto flex transform cursor-pointer items-center gap-3 rounded-md px-6 py-3 text-lg font-semibold shadow-2xl backdrop-blur-md transition-all hover:scale-105"
                        style={{ background: 'rgba(20, 20, 28, 0.85)', border: '1px solid rgba(255, 255, 255, 0.22)', color: '#fafafa' }}
                    >
                        Skip commercial
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z" /></svg>
                    </button>
                </div>
            )}

            {activeSkipPrompt && !showSkipButton && !isMinimized && !isEnding && (
                <div className="absolute bottom-32 right-12 z-[200] animate-fade-in-up">
                    <button
                        type="button"
                        onClick={(e) => {
                            e.stopPropagation();
                            seek(activeSkipPrompt.target);
                        }}
                        className="pointer-events-auto flex transform cursor-pointer items-center gap-3 rounded-md px-6 py-3 text-lg font-semibold shadow-2xl backdrop-blur-md transition-all hover:scale-105"
                        style={{ background: 'var(--vora-accent-500)', border: '1px solid var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                    >
                        {activeSkipPrompt.label}
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z" /></svg>
                    </button>
                </div>
            )}

            {!isMinimized && !isEnding && (
                <div
                    className={`absolute inset-0 z-30 flex flex-col justify-between transition-opacity duration-500 ${showControls || showSettings || showInfo ? 'opacity-100' : 'opacity-0'}`}
                    style={{
                        background: showControls || showSettings || showInfo
                            ? 'linear-gradient(180deg, rgba(0, 0, 0, 0.7) 0%, transparent 18%, transparent 70%, rgba(0, 0, 0, 0.85) 100%)'
                            : 'transparent',
                    }}
                >

                    <div className="flex items-start justify-between p-8">
                        <button
                            type="button"
                            onClick={closePlayer}
                            aria-label="Close player"
                            className="flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                            style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
                        </button>

                        <div className="flex max-w-3xl flex-col items-center text-center">
                            <h1 className="m-0 text-2xl font-semibold drop-shadow-lg" style={{ color: '#fafafa', letterSpacing: '-0.01em' }}>{currentMedia.title}</h1>
                            {currentMedia.subtitle && (
                                <p className="m-0 mt-1 drop-shadow-md" style={{ color: 'rgba(255, 255, 255, 0.78)' }}>{currentMedia.subtitle}</p>
                            )}

                            <div className="mt-3 flex flex-wrap items-center justify-center gap-1.5">
                                {displayRes && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={accentChipStyle}>{displayRes}</span>}
                                {displayHdr && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={{ ...chipStyle, color: '#facc15', borderColor: 'rgba(234, 179, 8, 0.45)' }}>{displayHdr}</span>}
                                {displayVideoCodec && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{displayVideoCodec}{currentMedia.videoStrategy === 'Transcode' ? ' (transcode)' : ''}</span>}
                                {displayAudioCodec && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{displayAudioCodec}{currentMedia.audioStrategy === 'Transcode' ? ' (transcode)' : ''}</span>}
                                {displayAudioChannels !== undefined && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={chipStyle}>{displayAudioChannels}ch</span>}
                                {currentMedia.subtitleTrackId && currentMedia.subtitleTrackId !== 'none' && activeSubtitleTrack && (
                                    <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>
                                        SUB: {activeSubtitleTrack.language?.slice(0, 3) || 'UNK'} {currentMedia.subtitleStrategy === 'BurnIn' ? '(Burn-in)' : `(${activeSubtitleTrack.codec})`}
                                    </span>
                                )}
                                {displayContainer && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{displayContainer}</span>}
                                {currentMedia.bandwidthKbps ? <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={chipStyle}>{(currentMedia.bandwidthKbps / 1000).toFixed(1)} Mbps</span> : null}
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMinimized(true)}
                            aria-label="Minimize to mini-player"
                            title="Minimize to mini-player"
                            className="flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                            style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                        </button>
                    </div>

                    <div className="mx-auto w-full max-w-screen-2xl p-8 pb-12">
                        <div className="mb-6 flex items-center gap-4 font-medium tabular-nums" style={{ color: '#fafafa' }}>
                            <span>{formatTime(currentTime)}</span>
                            <div
                                className="group relative h-1.5 flex-1 cursor-pointer rounded-full"
                                onClick={handleProgressClick}
                                style={{ background: 'rgba(255, 255, 255, 0.18)' }}
                            >
                                <MarkerBands markers={markers} duration={duration} />
                                <div className="absolute left-0 top-0 h-full rounded-full transition-all" style={{ width: `${progressPercent}%`, background: 'var(--vora-accent-500)' }} />
                                <div
                                    className="absolute top-1/2 -mt-1.5 h-3 w-3 rounded-full opacity-0 shadow-lg transition-opacity group-hover:opacity-100"
                                    style={{ left: `calc(${progressPercent}% - 6px)`, background: 'var(--vora-accent-500)' }}
                                />
                            </div>
                            <span>{formatTime(duration)}</span>
                        </div>

                        <div className="flex items-center justify-between" style={{ color: '#fafafa' }}>
                            <div className="flex w-1/3 items-center gap-6"></div>

                            <div className="flex w-1/3 items-center justify-center gap-8">
                                {isEpisode && (
                                    <EpisodeNavButton
                                        direction="previous"
                                        disabled={!upNextData?.previousItem}
                                        onClick={() => upNextData?.previousItem && handlePlayNext(upNextData.previousItem)}
                                        title={upNextData?.previousItem ? `Previous: ${upNextData.previousItem.title}` : 'No previous episode'}
                                    />
                                )}
                                <SkipButton seconds={10} direction="back" onClick={() => skipBackward(10)} />
                                <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} />
                                <SkipButton seconds={30} direction="forward" onClick={() => skipForward(30)} />
                                {isEpisode && (
                                    <EpisodeNavButton
                                        direction="next"
                                        disabled={!upNextData?.nextItem}
                                        onClick={() => upNextData?.nextItem && handlePlayNext(upNextData.nextItem)}
                                        title={upNextData?.nextItem ? `Next: ${upNextData.nextItem.title}` : 'No next episode'}
                                    />
                                )}
                            </div>

                            <div className="flex w-1/3 items-center justify-end gap-5">
                                <button
                                    type="button"
                                    onClick={() => setShowInfo(true)}
                                    aria-label="Media info"
                                    title="Media info"
                                    className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/10"
                                >
                                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" /></svg>
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setShowSettings(true)}
                                    aria-label="Quality and tracks"
                                    title="Quality &amp; tracks"
                                    className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/10"
                                >
                                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 9 19.4a1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 9a1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z" /></svg>
                                </button>

                                <VolumeControl value={volume} onChange={setVolume} />
                                <FullscreenButton onClick={toggleFullScreen} />
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {showSettings && (
                <PlayerSettingsPanel
                    mediaDetails={mediaDetails}
                    selVideo={selVideo}
                    selAudio={selAudio}
                    selSub={selSub}
                    setSelVideo={setSelVideo}
                    setSelAudio={setSelAudio}
                    setSelSub={setSelSub}
                    caps={caps}
                    onCancel={() => setShowSettings(false)}
                    onApply={handleApplyStreams}
                />
            )}

            {showInfo && (
                <PlayerInfoPanel
                    mediaDetails={mediaDetails}
                    onClose={() => setShowInfo(false)}
                />
            )}
        </div>
    );
}
