import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { useState, useRef, useCallback } from 'react';
import type { MediaMarker } from '../../api/Media/mediaService';
import { useAutoHideControls } from './Controls/useAutoHideControls';
import { usePictureClick } from './Controls/usePictureClick';
import { useFullscreen } from './Controls/useFullscreen';
import { usePlayerKeyboard } from './Controls/usePlayerKeyboard';
import PlayerKeyIndicator from './Controls/PlayerKeyIndicator';
import VideoSeekBar from './Controls/VideoSeekBar';
import { PlayPauseButton, SkipButton, VolumeControl, FullscreenButton, MaximizeButton, CloseButton, EpisodeNavButton, SubtitlesButton } from './Controls/PlayerButtons';
import PlayerSettingsPanel from './Panels/PlayerSettingsPanel';
import SubtitleTracksPanel from './Panels/SubtitleTracksPanel';
import MediaInfoDialog from '../Media/MediaInfoDialog';
import PlaybackStatsSections from './Panels/PlaybackStatsSections';
import UpNextOverlay from './Panels/UpNextOverlay';
import { isNoSubtitle, NoSubtitle } from '../../utils/subtitleKind';
import { ScrubPreview } from './VideoScrubThumbnails';
import { WebVideoEngine } from './engine/WebVideoEngine';
import { usePlaybackController } from './usePlaybackController';
import { useStreamRecovery } from './useStreamRecovery';

const chipStyle: React.CSSProperties = {
    background: 'color-mix(in srgb, var(--vora-bg-canvas) 72%, transparent)',
    border: '1px solid color-mix(in srgb, var(--vora-text-primary) 18%, transparent)',
    color: 'var(--vora-text-primary)',
    backdropFilter: 'blur(8px)',
    WebkitBackdropFilter: 'blur(8px)',
};

// Hues independent of the template accent — the progress fill IS the
// accent, so a marker band tinted with the accent is invisible against it.
const markerBandColors: Record<string, string> = {
    Intro: 'color-mix(in srgb, var(--vora-text-primary) 22%, transparent)',
    Recap: 'color-mix(in srgb, var(--vora-text-primary) 22%, transparent)',
    Credits: 'color-mix(in srgb, var(--vora-info-500) 32%, transparent)',
    CreditsScene: 'color-mix(in srgb, var(--vora-info-500) 32%, transparent)',
    Preview: 'color-mix(in srgb, color-mix(in srgb, var(--vora-info-500), var(--vora-danger-500)) 38%, transparent)',
};

function MarkerBands({ markers, duration }: { markers: MediaMarker[]; duration: number }) {
    if (!markers.length || !duration || !isFinite(duration) || duration <= 0) return null;
    return (
        <div className="pointer-events-none absolute inset-0 overflow-hidden rounded-full">
            {markers.map((m, idx) => {
                const start = Math.max(0, Math.min(100, (m.startSeconds / duration) * 100));
                const end = Math.max(0, Math.min(100, (m.endSeconds / duration) * 100));
                const width = Math.max(0.4, end - start);
                const bg = markerBandColors[m.type] ?? 'color-mix(in srgb, var(--vora-text-primary) 18%, transparent)';
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
    const {
        currentMedia, isPlaying, isMinimized, volume, sessionId,
        togglePlayPause, seek, skipForward, skipBackward, setMinimized, closePlayer, setVolume, videoRef,
    } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const playerContainerRef = useRef<HTMLDivElement>(null);
    const [engine] = useState(() => new WebVideoEngine(videoRef));
    const controller = usePlaybackController(engine);
    useStreamRecovery({
        engine,
        videoRef,
        stream: { key: currentMedia?.streamUrl ?? null, startPosition: currentMedia?.startPosition ?? 0 },
        restartStream: controller.restartStream,
        closePlayer,
    });
    const { upNextData, isEpisode, thumbnails, display, activeSubtitleTrack, visibleMarkers } = controller;

    const [showSettings, setShowSettings] = useState(false);
    const [showSubtitles, setShowSubtitles] = useState(false);
    const [showInfo, setShowInfo] = useState(false);
    const closeInfo = useCallback(() => setShowInfo(false), []);

    const showControls = useAutoHideControls({
        isMinimized,
        isPlaying,
        keepVisibleWhen: controller.isEnding || showSettings || showInfo || showSubtitles,
    });

    const toggleFullScreen = useFullscreen(playerContainerRef);

    const keyIndicator = usePlayerKeyboard({
        enabled: !!currentMedia && !isMinimized && !showSettings && !showSubtitles && !showInfo,
        canSeek: true,
        volume,
        isPlaying,
        onPlayPause: togglePlayPause,
        onSkipForward: skipForward,
        onSkipBackward: skipBackward,
        onVolume: setVolume,
    });

    const picture = usePictureClick({ isPlaying, onPlayPause: togglePlayPause });

    const [hoverPercent, setHoverPercent] = useState<number | null>(null);
    const [hoverBarRect, setHoverBarRect] = useState<DOMRect | null>(null);

    if (!currentMedia) return null;

    const formatTime = (seconds: number) => {
        if (isNaN(seconds) || !isFinite(seconds)) return '0:00';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = Math.floor(seconds % 60);
        if (h > 0) return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
        return `${m}:${s.toString().padStart(2, '0')}`;
    };

    const showScrubPreview = (percent: number | null, track: DOMRect | null) => {
        setHoverPercent(percent);
        setHoverBarRect(track);
    };

    const hoverCue = hoverPercent !== null ? thumbnails.findCue(hoverPercent * duration) : null;

    const handleApplyStreams = async () => {
        setShowSettings(false);
        await controller.applyStreams();
    };

    const handleSelectSubtitle = (subtitleTrackId: string) =>
        controller.selectSubtitle(subtitleTrackId, { onStreamRestart: () => setShowSubtitles(false) });

    return (
        <div
            ref={playerContainerRef}
            className={`transition-all duration-300 ease-in-out ${isMinimized
                ? 'fixed bottom-0 left-0 right-0 z-[99999] flex h-24 flex-col vora-glass'
                : 'fixed inset-0 z-[99999] bg-[var(--vora-bg-canvas)]'
                }`}
            style={isMinimized ? { borderTop: '1px solid var(--vora-border-subtle)' } : undefined}
        >

            <video
                key={currentMedia.sessionId}
                ref={videoRef}
                onClick={() => { if (isMinimized) setMinimized(false); }}
                className={`absolute bg-[var(--vora-bg-canvas)] transition-all duration-700 ease-in-out
                    ${isMinimized ? 'bottom-4 left-6 z-20 h-16 w-16 cursor-pointer rounded-md object-cover shadow-[var(--vora-shadow-lg)] hover:ring-2 hover:ring-[var(--vora-accent-500)] max-md:left-3' :
                        controller.isEnding ? 'left-20 top-32 z-20 aspect-video w-96 cursor-pointer rounded-xl object-cover shadow-[var(--vora-shadow-overlay)] max-md:left-4 max-md:top-4 max-md:w-40' :
                            'absolute left-0 top-0 z-0 h-[100vh] w-full object-contain'}`}
                style={controller.isEnding && !isMinimized ? { border: '1px solid var(--vora-border-strong)' } : undefined}
            />

            {controller.isEnding && !isMinimized && (
                <UpNextOverlay
                    currentMedia={{ title: currentMedia.title, subtitle: currentMedia.subtitle }}
                    upNextData={controller.upNextData}
                    onPlayNext={controller.playNext}
                    onClose={closePlayer}
                    autoPlay={controller.autoPlayNext}
                    finished={controller.finished}
                    secondsLeft={controller.finished || !isFinite(duration) ? 0 : Math.max(0, duration - currentTime)}
                    onCancelAutoPlay={controller.cancelAutoPlay}
                />
            )}

            <ScrubPreview
                hoverPercent={hoverPercent}
                duration={duration}
                barRect={hoverBarRect}
                cue={thumbnails.available ? hoverCue : null}
                spriteUrl={thumbnails.spriteUrl}
                width={thumbnails.width}
                height={thumbnails.height}
            />

            {isMinimized && (
                <div className="z-10 flex h-full w-full flex-1 items-center justify-between px-6 max-md:gap-2 max-md:px-3">
                    <div className="flex w-1/4 items-center gap-4 max-md:w-auto max-md:min-w-0 max-md:flex-1 max-md:gap-3">
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

                    <div className="flex max-w-2xl flex-1 flex-col items-center justify-center px-8 max-md:flex-none max-md:px-0">
                        <div className="mb-2 flex items-center gap-6 max-md:mb-0">
                            <div className="contents max-md:hidden">
                                {isEpisode && (
                                    <EpisodeNavButton
                                        direction="previous"
                                        size="sm"
                                        disabled={!upNextData?.previousItem}
                                        onClick={() => upNextData?.previousItem && controller.playNext(upNextData.previousItem)}
                                        title={upNextData?.previousItem ? `Previous: ${upNextData.previousItem.title}` : 'No previous episode'}
                                    />
                                )}
                                <SkipButton seconds={10} direction="back" size="sm" onClick={() => skipBackward(10)} />
                            </div>
                            <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} size="sm" />
                            <div className="contents max-md:hidden">
                                <SkipButton seconds={30} direction="forward" size="sm" onClick={() => skipForward(30)} />
                                {isEpisode && (
                                    <EpisodeNavButton
                                        direction="next"
                                        size="sm"
                                        disabled={!upNextData?.nextItem}
                                        onClick={() => upNextData?.nextItem && controller.playNext(upNextData.nextItem)}
                                        title={upNextData?.nextItem ? `Next: ${upNextData.nextItem.title}` : 'No next episode'}
                                    />
                                )}
                            </div>
                        </div>
                        <div className="flex w-full items-center gap-3 text-xs font-medium tabular-nums max-md:hidden" style={{ color: 'var(--vora-text-muted)' }}>
                            <span>{formatTime(currentTime)}</span>
                            <VideoSeekBar variant="mini" currentTime={currentTime} duration={duration} onSeek={seek} onPointAt={showScrubPreview}>
                                <MarkerBands markers={visibleMarkers} duration={duration} />
                            </VideoSeekBar>
                            <span>{formatTime(duration)}</span>
                        </div>
                    </div>

                    <div className="flex w-1/4 items-center justify-end gap-4 max-md:w-auto max-md:gap-1">
                        <MaximizeButton onClick={() => setMinimized(false)} />
                        <CloseButton onClick={closePlayer} />
                    </div>
                </div>
            )}

            {controller.showSkipCommercial && !isMinimized && !controller.isEnding && (
                <div className="absolute bottom-32 right-12 z-[200] animate-fade-in-up">
                    <button
                        type="button"
                        onClick={(e) => {
                            e.stopPropagation();
                            controller.skipCommercial();
                        }}
                        className="pointer-events-auto flex transform cursor-pointer items-center gap-3 rounded-md px-6 py-3 text-lg font-semibold shadow-[var(--vora-shadow-overlay)] backdrop-blur-md transition-all hover:scale-105"
                        style={{ background: 'color-mix(in srgb, var(--vora-bg-canvas) 85%, transparent)', border: '1px solid color-mix(in srgb, var(--vora-text-primary) 22%, transparent)', color: 'var(--vora-text-primary)' }}
                    >
                        Skip commercial
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z" /></svg>
                    </button>
                </div>
            )}

            {controller.activeSkipPrompt && !controller.showSkipCommercial && !isMinimized && !controller.isEnding && (
                <div className="absolute bottom-32 right-12 z-[200] animate-fade-in-up">
                    <button
                        type="button"
                        onClick={(e) => {
                            e.stopPropagation();
                            controller.skipToMarker();
                        }}
                        className="pointer-events-auto flex transform cursor-pointer items-center gap-3 rounded-md px-6 py-3 text-lg font-semibold shadow-[var(--vora-shadow-overlay)] backdrop-blur-md transition-all hover:scale-105"
                        style={{ background: 'var(--vora-accent-500)', border: '1px solid var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                    >
                        {controller.activeSkipPrompt.label}
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z" /></svg>
                    </button>
                </div>
            )}

            {!isMinimized && !controller.isEnding && (
                <div
                    data-testid="player-picture"
                    onPointerDown={picture.onPointerDown}
                    onClick={picture.onClick}
                    className={`absolute inset-0 z-30 flex flex-col justify-between transition-opacity duration-500 ${showControls || showSettings || showInfo || showSubtitles ? 'opacity-100' : 'opacity-0'}`}
                    style={{
                        background: showControls || showSettings || showInfo || showSubtitles
                            ? 'linear-gradient(180deg, color-mix(in srgb, var(--vora-bg-canvas) 70%, transparent) 0%, transparent 18%, transparent 70%, color-mix(in srgb, var(--vora-bg-canvas) 85%, transparent) 100%)'
                            : 'transparent',
                    }}
                >

                    <div className="flex items-start justify-between p-8 max-md:gap-3 max-md:p-4">
                        <button
                            type="button"
                            onClick={closePlayer}
                            aria-label="Close player"
                            className="vora-icon-button flex h-12 w-12 cursor-pointer items-center justify-center rounded-full bg-[color-mix(in_srgb,var(--vora-bg-canvas)_55%,transparent)] backdrop-blur-md transition-colors max-md:h-10 max-md:w-10 max-md:shrink-0"
                            style={{ border: '1px solid color-mix(in srgb, var(--vora-text-primary) 16%, transparent)', color: 'var(--vora-text-primary)' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
                        </button>

                        <div className="flex max-w-3xl flex-col items-center text-center">
                            <h1 className="m-0 text-2xl font-semibold drop-shadow-[0_4px_4px_color-mix(in_srgb,var(--vora-bg-canvas)_70%,transparent)] max-md:text-lg" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{currentMedia.title}</h1>
                            {currentMedia.subtitle && (
                                <p className="m-0 mt-1 drop-shadow-[0_3px_3px_color-mix(in_srgb,var(--vora-bg-canvas)_60%,transparent)]" style={{ color: 'color-mix(in srgb, var(--vora-text-primary) 78%, transparent)' }}>{currentMedia.subtitle}</p>
                            )}

                            <div className="mt-3 flex flex-wrap items-center justify-center gap-1.5">
                                {display.resolution && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={chipStyle}>{display.resolution}</span>}
                                {display.hdr && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={{ ...chipStyle, color: 'var(--vora-warning-text)', borderColor: 'color-mix(in srgb, var(--vora-warning-500) 45%, transparent)' }}>{display.hdr}</span>}
                                {display.videoCodec && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{display.videoCodec}{currentMedia.videoStrategy === 'Transcode' ? ' (transcode)' : ''}</span>}
                                {display.audioCodec && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{display.audioCodec}{currentMedia.audioStrategy === 'Transcode' ? ' (transcode)' : ''}</span>}
                                {display.audioChannels !== undefined && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={chipStyle}>{display.audioChannels}ch</span>}
                                {controller.isLoadingSubtitle && (
                                    <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>
                                        Loading subtitles…
                                    </span>
                                )}
                                {!controller.isLoadingSubtitle && !isNoSubtitle(controller.effectiveSubtitleId) && activeSubtitleTrack && (
                                    <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>
                                        SUB: {activeSubtitleTrack.language?.slice(0, 3) || 'UNK'} {currentMedia.subtitleStrategy === 'BurnIn' ? '(Burn-in)' : `(${activeSubtitleTrack.codec})`}
                                    </span>
                                )}
                                {display.container && <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold uppercase" style={chipStyle}>{display.container}</span>}
                                {currentMedia.bandwidthKbps ? <span className="rounded-md px-2 py-0.5 text-[11px] font-semibold" style={chipStyle}>{(currentMedia.bandwidthKbps / 1000).toFixed(1)} Mbps</span> : null}
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMinimized(true)}
                            aria-label="Minimize to mini-player"
                            title="Minimize to mini-player"
                            className="vora-icon-button flex h-12 w-12 cursor-pointer items-center justify-center rounded-full bg-[color-mix(in_srgb,var(--vora-bg-canvas)_55%,transparent)] backdrop-blur-md transition-colors max-md:h-10 max-md:w-10 max-md:shrink-0"
                            style={{ border: '1px solid color-mix(in srgb, var(--vora-text-primary) 16%, transparent)', color: 'var(--vora-text-primary)' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                        </button>
                    </div>

                    <div className="mx-auto w-full max-w-screen-2xl p-8 pb-12 max-md:px-4 max-md:pt-4 max-md:pb-6">
                        <div className="mb-6 flex items-center gap-4 font-medium tabular-nums max-md:mb-4 max-md:gap-3 max-md:text-sm" style={{ color: 'var(--vora-text-primary)' }}>
                            <span>{formatTime(currentTime)}</span>
                            <VideoSeekBar variant="full" currentTime={currentTime} duration={duration} onSeek={seek} onPointAt={showScrubPreview}>
                                <MarkerBands markers={visibleMarkers} duration={duration} />
                            </VideoSeekBar>
                            <span>{formatTime(duration)}</span>
                        </div>

                        <div className="flex items-center justify-between max-md:flex-wrap max-md:gap-y-3" style={{ color: 'var(--vora-text-primary)' }}>
                            <div className="flex w-1/3 items-center gap-6 max-md:hidden"></div>

                            <div className="flex w-1/3 items-center justify-center gap-8 max-md:w-full max-md:gap-6">
                                {isEpisode && (
                                    <EpisodeNavButton
                                        direction="previous"
                                        disabled={!upNextData?.previousItem}
                                        onClick={() => upNextData?.previousItem && controller.playNext(upNextData.previousItem)}
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
                                        onClick={() => upNextData?.nextItem && controller.playNext(upNextData.nextItem)}
                                        title={upNextData?.nextItem ? `Next: ${upNextData.nextItem.title}` : 'No next episode'}
                                    />
                                )}
                            </div>

                            <div className="flex w-1/3 items-center justify-end gap-5 max-md:w-full max-md:justify-center max-md:gap-4">
                                <button
                                    type="button"
                                    onClick={() => setShowInfo(true)}
                                    aria-label="Media info"
                                    title="Media info"
                                    className="cursor-pointer rounded-full p-2 vora-icon-button transition-colors"
                                >
                                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" /></svg>
                                </button>
                                {controller.hasSubtitleOptions && (
                                    <SubtitlesButton
                                        onClick={() => setShowSubtitles(true)}
                                        isActive={!isNoSubtitle(controller.effectiveSubtitleId)}
                                    />
                                )}
                                <button
                                    type="button"
                                    onClick={() => setShowSettings(true)}
                                    aria-label="Quality and tracks"
                                    title="Quality &amp; tracks"
                                    className="cursor-pointer rounded-full p-2 vora-icon-button transition-colors"
                                >
                                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 9 19.4a1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 9a1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z" /></svg>
                                </button>

                                <div className="contents max-md:hidden">
                                    <VolumeControl value={volume} onChange={setVolume} />
                                </div>
                                <FullscreenButton onClick={toggleFullScreen} />
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {!isMinimized && <PlayerKeyIndicator indicator={picture.indicator ?? keyIndicator} />}

            {showSettings && (
                <PlayerSettingsPanel
                    mediaDetails={controller.mediaDetails}
                    selVideo={controller.selVideo}
                    selAudio={controller.selAudio}
                    setSelVideo={controller.setSelVideo}
                    setSelAudio={controller.setSelAudio}
                    caps={controller.caps}
                    onVideoPartChanged={() => handleSelectSubtitle(NoSubtitle)}
                    onCancel={() => setShowSettings(false)}
                    onApply={handleApplyStreams}
                />
            )}

            {showSubtitles && (
                <SubtitleTracksPanel
                    mediaDetails={controller.mediaDetails}
                    activePart={controller.activeStreamPart}
                    selectedSubtitleId={controller.selSub}
                    mediaItemId={currentMedia.id}
                    serverId={controller.serverId}
                    canFindSubtitles={controller.canFindSubtitles}
                    onSelect={handleSelectSubtitle}
                    onSubtitleDownloaded={controller.subtitleDownloaded}
                    onClose={() => setShowSubtitles(false)}
                />
            )}

            {showInfo && (
                <MediaInfoDialog
                    title="Playback info"
                    parts={controller.activeStreamPart ? [controller.activeStreamPart] : []}
                    placement="absolute"
                    onClose={closeInfo}
                    lead={(
                        <PlaybackStatsSections
                            sessionId={sessionId}
                            serverId={controller.serverId}
                            reasons={currentMedia.reasons}
                            readPlayer={controller.readPlayer}
                            sideloadedSubtitle={currentMedia.subtitleStrategy !== 'BurnIn' && !isNoSubtitle(controller.effectiveSubtitleId) && activeSubtitleTrack
                                ? { language: activeSubtitleTrack.language, title: activeSubtitleTrack.title, codec: activeSubtitleTrack.codec }
                                : null}
                        />
                    )}
                />
            )}
        </div>
    );
}
