import { useEffect, useState, useRef, useMemo } from 'react';
import type Hls from 'hls.js';
import { loadHls } from '../../utils/loadHls';
import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { type IptvChannelVM } from '../../api/Iptv/iptvAdminService';
import { iptvClientService } from '../../api/Iptv/iptvClientService';
import { timeshiftService } from '../../api/Iptv/timeshiftService';
import { passthroughService } from '../../api/Iptv/passthroughService';
import { podcastService } from '../../api/Podcasts/podcastService';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys, decodeJwtPayload, getProfileIdFromToken } from '../../utils/storageKeys';
import { PlayPauseButton, SkipButton, VolumeControl, MaximizeButton, CloseButton } from './Controls/PlayerButtons';

export default function LiveRadioPlayer() {
    const { currentMedia, isPlaying, isMinimized, volume, togglePlayPause, setMinimized, closePlayer, setVolume, videoRef, playMedia, skipForward, skipBackward, seek, nextTrack, previousTrack, hasNext, hasPrevious, queue, queueIndex, jumpToQueueIndex, isShuffled, toggleShuffle, repeatMode, cycleRepeatMode, setFullscreen, isFullscreen } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const [showQueue, setShowQueue] = useState(false);

    const playerContainerRef = useRef<HTMLDivElement>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [streamError, setStreamError] = useState<string | null>(null);
    const [channels, setChannels] = useState<IptvChannelVM[]>([]);

    const isPodcast = currentMedia?.playbackContextType === 'Podcast';
    const isMusic = currentMedia?.playbackContextType === 'Music';
    const isAudioOnDemand = isPodcast || isMusic;

    const canTimeshift = useMemo(() => {
        const token = localStorage.getItem(StorageKeys.profileToken);
        if (!token) return false;
        try {
            const payload = decodeJwtPayload(token);
            return payload?.canTimeshiftIptv === 'True';
        } catch {
            return false;
        }
    }, []);

    useEffect(() => {
        if (currentMedia?.playbackContextType !== 'LiveRadio') {
            queueMicrotask(() => setChannels([]));
            return;
        }

        let cancelled = false;
        const loadRadioChannels = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;
                const userId = localStorage.getItem(StorageKeys.userId) || activeProfileId;

                const allProviders = await iptvClientService.getPlaylists(userId, activeProfileId, activeServer.id);
                if (cancelled) return;
                const radioChannels = allProviders.flatMap(p => p.channels || []).filter(c => c.kind === 'Radio');
                setChannels(radioChannels);
            } catch (error) {
                console.error("Failed to load radio channels for player", error);
            }
        };
        loadRadioChannels();
        return () => { cancelled = true; };
    }, [currentMedia?.playbackContextType]);

    useEffect(() => {
        const video = videoRef.current;
        if (!video || !currentMedia?.id) return;

        let hls: Hls | null = null;
        let isMounted = true;
        queueMicrotask(() => { if (isMounted) { setIsLoading(true); setStreamError(null); } });

        if (isAudioOnDemand) {
            const startPosition = currentMedia.startPosition ?? 0;
            video.src = currentMedia.streamUrl;

            const onLoadedMetadata = () => {
                if (!isMounted) return;
                if (startPosition > 5 && (video.duration === 0 || startPosition < video.duration - 5)) {
                    try {
                        video.currentTime = startPosition;
                    } catch (e) {
                        console.warn("Failed to seek to saved position", e);
                    }
                }
                video.play().catch(e => console.error(e));
            };
            const onPlaying = () => {
                if (isMounted) setIsLoading(false);
            };

            video.addEventListener('loadedmetadata', onLoadedMetadata);
            video.addEventListener('playing', onPlaying);

            return () => {
                isMounted = false;
                video.removeEventListener('loadedmetadata', onLoadedMetadata);
                video.removeEventListener('playing', onPlaying);
                video.removeAttribute('src');
                video.load();
            };
        }

        // Radio streams can fail silently (dead URL, CORS block, network drop).
        // Without these the loading spinner spins forever. Surface a message and
        // stop the spinner on any fatal HLS error, media error, or timeout.
        const STREAM_ERROR = 'This station could not be played. It may be offline or blocking playback.';
        let loadTimeout: ReturnType<typeof setTimeout>;
        const failStream = (message: string) => {
            if (!isMounted) return;
            clearTimeout(loadTimeout);
            setIsLoading(false);
            setStreamError(message);
        };
        const onVideoPlaying = () => {
            if (!isMounted) return;
            clearTimeout(loadTimeout);
            setStreamError(null);
            setIsLoading(false);
        };
        const onVideoError = () => failStream(STREAM_ERROR);
        video.addEventListener('playing', onVideoPlaying);
        video.addEventListener('error', onVideoError);
        loadTimeout = setTimeout(() => failStream('This station is not responding. Try another station.'), 20000);

        const attachPassthrough = async () => {
            const activeServer = serverVault.getActiveServer();
            let streamUrl: string;
            let streamType: 'hls' | 'audio';
            try {
                const data = await passthroughService.startPassthrough(currentMedia.id, activeServer?.id);
                if (!isMounted) return;
                streamUrl = `${import.meta.env.VITE_API_URL || ''}${data.url}`;
                streamType = data.streamType;
            } catch (err) {
                console.error("Failed to start radio passthrough:", err);
                failStream(STREAM_ERROR);
                return;
            }

            if (streamType === 'audio') {
                video.src = streamUrl;
                video.play().catch(e => console.error(e));
                const onPlaying = () => { setIsLoading(false); video.removeEventListener('playing', onPlaying); };
                video.addEventListener('playing', onPlaying);
                return;
            }

            const HlsClass = await loadHls();
            if (!isMounted) return;

            if (HlsClass.isSupported()) {
                hls = new HlsClass();
                hls.loadSource(streamUrl);
                hls.attachMedia(video);
                hls.on(HlsClass.Events.MANIFEST_PARSED, () => {
                    if (isMounted) {
                        video.play().catch(e => console.error(e));
                        setIsLoading(false);
                    }
                });
                hls.on(HlsClass.Events.ERROR, (_e, data) => {
                    if (data.fatal) failStream(STREAM_ERROR);
                });
            } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                video.src = streamUrl;
                video.play().catch(e => console.error(e));
                setIsLoading(false);
            }
        };

        const initializeStream = async () => {
            if (!canTimeshift) {
                await attachPassthrough();
                return;
            }

            try {
                const activeServer = serverVault.getActiveServer();
                const data = await timeshiftService.startTimeshift(currentMedia.id, activeServer?.id);
                if (!isMounted) return;

                const finalUrl = `${import.meta.env.VITE_API_URL || ''}${data.url}`;

                const HlsClass = await loadHls();
                if (!isMounted) return;

                if (HlsClass.isSupported()) {
                    hls = new HlsClass({
                        enableWorker: true,
                        lowLatencyMode: false,
                        liveSyncDurationCount: 3
                    });
                    hls.loadSource(finalUrl);
                    hls.attachMedia(video);
                    hls.on(HlsClass.Events.MANIFEST_PARSED, () => {
                        if (isMounted) {
                            video.play().catch(e => console.error(e));
                            setIsLoading(false);
                        }
                    });
                    hls.on(HlsClass.Events.ERROR, (_e, data) => {
                        if (data.fatal) failStream(STREAM_ERROR);
                    });
                } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                    video.src = finalUrl;
                    video.play().catch(e => console.error(e));
                    setIsLoading(false);
                }
            } catch (err) {
                if (!isMounted) return;
                console.error("Radio timeshift failed, falling back to passthrough:", err);
                await attachPassthrough();
            }
        };

        initializeStream();

        const pingInterval = canTimeshift ? setInterval(() => {
            const activeServer = serverVault.getActiveServer();
            timeshiftService.pingTimeshift(activeServer?.id).catch(() => { });
        }, 30000) : null;

        return () => {
            isMounted = false;
            clearTimeout(loadTimeout);
            video.removeEventListener('playing', onVideoPlaying);
            video.removeEventListener('error', onVideoError);
            if (pingInterval) clearInterval(pingInterval);
            if (hls) hls.destroy();
            video.removeAttribute('src');
            video.load();
            if (canTimeshift) {
                const activeServer = serverVault.getActiveServer();
                timeshiftService.stopTimeshift(activeServer?.id).catch(() => { });
            }
        };
    }, [currentMedia?.id, videoRef, canTimeshift, isAudioOnDemand, currentMedia?.streamUrl, currentMedia?.startPosition]);

    useEffect(() => {
        if (!isPodcast || !currentMedia?.id) return;
        const episodeId = currentMedia.id;
        const activeServer = serverVault.getActiveServer();

        const computeIsPlayed = (video: HTMLVideoElement): boolean => {
            if (!video.duration || !isFinite(video.duration)) return false;
            return video.currentTime >= video.duration - 30;
        };

        const saveAndBroadcast = (position: number, isPlayed: boolean, explicit?: boolean) => {
            podcastService
                .saveEpisodeState(episodeId, position, explicit ? isPlayed : undefined, activeServer?.id)
                .catch(err => console.warn("Failed to save episode state", err));
            window.dispatchEvent(new CustomEvent('podcast:episode-state-changed', {
                detail: { episodeId, positionSeconds: position, isPlayed }
            }));
        };

        const intervalId = setInterval(() => {
            const video = videoRef.current;
            if (!video || video.paused) return;
            const pos = video.currentTime;
            if (pos < 1) return;
            saveAndBroadcast(pos, computeIsPlayed(video));
        }, 10000);

        const flushOnPause = () => {
            const video = videoRef.current;
            if (!video) return;
            const pos = video.currentTime;
            if (pos < 1) return;
            saveAndBroadcast(pos, computeIsPlayed(video));
        };
        const flushOnEnded = () => {
            const video = videoRef.current;
            if (!video) return;
            saveAndBroadcast(video.duration || video.currentTime, true, true);
        };

        const video = videoRef.current;
        video?.addEventListener('pause', flushOnPause);
        video?.addEventListener('ended', flushOnEnded);

        return () => {
            clearInterval(intervalId);
            video?.removeEventListener('pause', flushOnPause);
            video?.removeEventListener('ended', flushOnEnded);
            const pos = video?.currentTime ?? 0;
            if (pos >= 1 && video) {
                saveAndBroadcast(pos, computeIsPlayed(video));
            }
        };
    }, [isPodcast, currentMedia?.id, videoRef]);

    const formatTime = (seconds: number): string => {
        if (!isFinite(seconds) || seconds < 0) return '0:00';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = Math.floor(seconds % 60);
        if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
        return `${m}:${String(s).padStart(2, '0')}`;
    };

    const handleChannelChange = (direction: 'next' | 'prev') => {
        if (!channels.length || !currentMedia) return;
        const currentIndex = channels.findIndex(c => c.id === currentMedia.id);
        if (currentIndex === -1) return;

        let newIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
        if (newIndex >= channels.length) newIndex = 0;
        if (newIndex < 0) newIndex = channels.length - 1;

        const nextChannel = channels[newIndex];
        setIsLoading(true);
        playMedia({
            ...currentMedia,
            id: nextChannel.id,
            title: nextChannel.name,
            subtitle: nextChannel.groupTitle || 'Live Radio',
            posterUrl: nextChannel.logoUrl,
            streamUrl: nextChannel.streamUrl,
            container: 'hls',
            playbackContextType: 'LiveRadio'
        });
    };

    if (!currentMedia) return null;

    const hiddenForFullscreen = isFullscreen && currentMedia.playbackContextType === 'Music';

    const containerClass = hiddenForFullscreen
        ? 'fixed bottom-0 left-0 right-0 h-0 overflow-hidden pointer-events-none z-[1]'
        : `transition-all duration-300 ease-in-out ${isMinimized ? 'fixed bottom-0 left-0 right-0 z-[99999] flex h-24 flex-col vora-glass' : 'fixed inset-0 z-[99999]'}`;

    const containerStyle: React.CSSProperties | undefined = hiddenForFullscreen
        ? undefined
        : isMinimized
            ? { borderTop: '1px solid var(--vora-border-subtle)' }
            : { background: 'var(--vora-bg-canvas)' };

    return (
        <div ref={playerContainerRef} className={containerClass} style={containerStyle}>

            <video ref={videoRef} autoPlay playsInline className="hidden" />

            {!hiddenForFullscreen && (isMinimized ? (
                <div className="flex h-full w-full items-center justify-between px-6">
                    <div className="flex min-w-0 flex-1 items-center gap-4">
                        <div
                            className="flex h-16 w-16 shrink-0 cursor-pointer items-center justify-center overflow-hidden rounded-md transition-all hover:ring-2"
                            onClick={() => isMusic ? setFullscreen(true) : setMinimized(false)}
                            title={isMusic ? 'Open Now Playing' : 'Expand player'}
                            style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                        >
                            {currentMedia.posterUrl
                                ? <img src={currentMedia.posterUrl} alt={currentMedia.title} className="max-h-full max-w-full object-contain" />
                                : <svg width="32" height="32" fill="currentColor" viewBox="0 0 24 24" style={{ color: 'var(--vora-text-disabled)' }}><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                        </div>
                        <div className="flex flex-col overflow-hidden">
                            <span
                                className="cursor-pointer truncate font-semibold hover:underline"
                                onClick={() => isMusic ? setFullscreen(true) : setMinimized(false)}
                                style={{ color: 'var(--vora-text-primary)' }}
                            >
                                {currentMedia.title}
                            </span>
                            <span className="truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                                {currentMedia.subtitle || 'Live Radio'}
                            </span>
                        </div>
                        {isMusic && (
                            <button
                                type="button"
                                onClick={() => setFullscreen(true)}
                                title="Open Now Playing"
                                className="ml-2 inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><path d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" /></svg>
                            </button>
                        )}
                    </div>

                    <div className="mx-8 flex items-center gap-4">
                        {!isAudioOnDemand && (
                            <button
                                type="button"
                                onClick={() => handleChannelChange('prev')}
                                title="Previous station"
                                className="cursor-pointer transition-colors"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </button>
                        )}
                        {isMusic && (
                            <button
                                type="button"
                                onClick={previousTrack}
                                disabled={!hasPrevious && currentTime <= 3}
                                title="Previous track"
                                className="cursor-pointer transition-colors disabled:cursor-not-allowed disabled:opacity-30"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </button>
                        )}
                        {(isAudioOnDemand || canTimeshift) && <SkipButton seconds={10} direction="back" size="sm" onClick={() => skipBackward(10)} />}
                        <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} size="sm" />
                        {(isAudioOnDemand || canTimeshift) && <SkipButton seconds={30} direction="forward" size="sm" onClick={() => skipForward(30)} />}
                        {!isAudioOnDemand && (
                            <button
                                type="button"
                                onClick={() => handleChannelChange('next')}
                                title="Next station"
                                className="cursor-pointer transition-colors"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                            </button>
                        )}
                        {isMusic && (
                            <button
                                type="button"
                                onClick={nextTrack}
                                disabled={!hasNext}
                                title="Next track"
                                className="cursor-pointer transition-colors disabled:cursor-not-allowed disabled:opacity-30"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                            </button>
                        )}
                        {!isAudioOnDemand && (
                            <div className="ml-2 flex items-center gap-2">
                                <span className="h-2 w-2 animate-pulse rounded-full" style={{ background: 'var(--vora-accent-500)' }} />
                                <span className="text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>On air</span>
                            </div>
                        )}
                    </div>

                    <div className="flex items-center gap-3">
                        <VolumeControl value={volume} onChange={setVolume} />
                        <MaximizeButton onClick={() => setMinimized(false)} />
                        <CloseButton onClick={closePlayer} />
                    </div>
                </div>
            ) : (
                <div className="absolute inset-0 flex flex-col items-center justify-center px-8">
                    <div className="absolute top-6 left-6 right-6 flex items-center justify-between">
                        <button
                            type="button"
                            onClick={closePlayer}
                            aria-label="Close player"
                            className="flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                            style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
                        </button>
                        <button
                            type="button"
                            onClick={() => setMinimized(true)}
                            aria-label="Minimize"
                            className="flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                            style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                        </button>
                    </div>

                    <div
                        className="mb-10 flex h-72 w-72 items-center justify-center overflow-hidden rounded-2xl md:h-96 md:w-96"
                        style={{
                            background: 'var(--vora-bg-surface)',
                            border: '1px solid var(--vora-border-subtle)',
                            boxShadow: 'var(--vora-shadow-overlay)',
                        }}
                    >
                        {currentMedia.posterUrl
                            ? <img src={currentMedia.posterUrl} alt={currentMedia.title} className="max-h-full max-w-full object-contain p-8" />
                            : <svg width="160" height="160" fill="currentColor" viewBox="0 0 24 24" style={{ color: 'var(--vora-text-disabled)' }}><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>}
                    </div>

                    <h1 className="m-0 mb-1 line-clamp-2 max-w-3xl text-center text-4xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{currentMedia.title}</h1>
                    <p className="m-0 mb-2 text-center" style={{ color: 'var(--vora-text-secondary)' }}>{currentMedia.subtitle || (isPodcast ? 'Podcast' : isMusic ? 'Music' : 'Live Radio')}</p>
                    {!isAudioOnDemand && (
                        <div className="mb-10 flex items-center gap-2">
                            <span className="h-2 w-2 animate-pulse rounded-full" style={{ background: 'var(--vora-accent-500)' }} />
                            <span className="text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>On air</span>
                        </div>
                    )}

                    {isAudioOnDemand && (
                        <div className="mb-8 mt-4 w-full max-w-2xl">
                            <input
                                type="range"
                                min={0}
                                max={duration || 100}
                                value={currentTime}
                                onChange={e => seek(Number(e.target.value))}
                                aria-label="Playback position"
                                className="h-1 w-full cursor-pointer appearance-none rounded-lg accent-[var(--vora-accent-500)]"
                                style={{ background: 'rgba(255, 255, 255, 0.14)' }}
                            />
                            <div className="mt-1 flex justify-between text-xs tabular-nums" style={{ color: 'var(--vora-text-muted)' }}>
                                <span>{formatTime(currentTime)}</span>
                                <span>{duration ? `-${formatTime(Math.max(0, duration - currentTime))}` : ''}</span>
                            </div>
                        </div>
                    )}

                    <div className="flex items-center gap-8">
                        {!isAudioOnDemand && (
                            <button
                                type="button"
                                onClick={() => handleChannelChange('prev')}
                                title="Previous station"
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5"
                                style={{ color: 'var(--vora-text-secondary)' }}
                            >
                                <svg className="h-10 w-10 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </button>
                        )}
                        {isMusic && (
                            <button
                                type="button"
                                onClick={previousTrack}
                                disabled={!hasPrevious && currentTime <= 3}
                                title="Previous track"
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5 disabled:cursor-not-allowed disabled:opacity-30"
                                style={{ color: 'var(--vora-text-secondary)' }}
                            >
                                <svg className="h-10 w-10 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </button>
                        )}
                        {(isAudioOnDemand || canTimeshift) && <SkipButton seconds={10} direction="back" onClick={() => skipBackward(10)} />}
                        <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} />
                        {(isAudioOnDemand || canTimeshift) && <SkipButton seconds={30} direction="forward" onClick={() => skipForward(30)} />}
                        {!isAudioOnDemand && (
                            <button
                                type="button"
                                onClick={() => handleChannelChange('next')}
                                title="Next station"
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5"
                                style={{ color: 'var(--vora-text-secondary)' }}
                            >
                                <svg className="h-10 w-10 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                            </button>
                        )}
                        {isMusic && (
                            <button
                                type="button"
                                onClick={nextTrack}
                                disabled={!hasNext}
                                title="Next track"
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5 disabled:cursor-not-allowed disabled:opacity-30"
                                style={{ color: 'var(--vora-text-secondary)' }}
                            >
                                <svg className="h-10 w-10 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                            </button>
                        )}
                    </div>

                    {isMusic && (
                        <div className="mt-8 flex items-center gap-6">
                            <button
                                type="button"
                                onClick={toggleShuffle}
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5"
                                title={isShuffled ? 'Shuffle: On' : 'Shuffle: Off'}
                                style={{ color: isShuffled ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                            >
                                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                            </button>
                            <button
                                type="button"
                                onClick={cycleRepeatMode}
                                className="relative cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5"
                                title={`Repeat: ${repeatMode === 'off' ? 'Off' : repeatMode === 'all' ? 'All' : 'One'}`}
                                style={{ color: repeatMode !== 'off' ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                            >
                                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 4v5h5M20 20v-5h-5M4 9a9 9 0 0114.85-3.36L20 7M20 15a9 9 0 01-14.85 3.36L4 17" /></svg>
                                {repeatMode === 'one' && (
                                    <span
                                        className="absolute -right-0.5 -top-0.5 flex h-4 w-4 items-center justify-center rounded-full text-[10px] font-bold leading-none"
                                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                                    >
                                        1
                                    </span>
                                )}
                            </button>
                            <button
                                type="button"
                                onClick={() => setShowQueue(s => !s)}
                                className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/5"
                                title="Show queue"
                                style={{ color: showQueue ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                            >
                                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 6h16M4 12h10M4 18h7M19 12v6m-3-3l3 3 3-3" /></svg>
                            </button>
                        </div>
                    )}

                    <div className="mt-10">
                        <VolumeControl value={volume} onChange={setVolume} />
                    </div>

                    {isMusic && showQueue && queue.length > 0 && (
                        <div
                            className="fixed right-0 top-0 z-30 flex h-full w-full flex-col sm:w-96"
                            style={{ background: 'var(--vora-bg-surface)', borderLeft: '1px solid var(--vora-border-subtle)', boxShadow: 'var(--vora-shadow-overlay)' }}
                        >
                            <div className="flex items-center justify-between px-5 py-4" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                                <div>
                                    <h3 className="m-0 text-sm font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>Queue</h3>
                                    <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>{queue.length} tracks</p>
                                </div>
                                <button
                                    type="button"
                                    onClick={() => setShowQueue(false)}
                                    title="Close queue"
                                    aria-label="Close queue"
                                    className="flex h-9 w-9 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                                    style={{ background: 'var(--vora-bg-raised)', color: 'var(--vora-text-muted)' }}
                                >
                                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                                </button>
                            </div>
                            <div className="flex-1 overflow-y-auto py-2">
                                {queue.map((item, idx) => {
                                    const isCurrent = idx === queueIndex;
                                    return (
                                        <button
                                            key={`${item.id}-${idx}`}
                                            type="button"
                                            onClick={() => jumpToQueueIndex(idx)}
                                            className="flex w-full cursor-pointer items-center gap-3 px-5 py-2.5 text-left transition-colors hover:bg-white/5"
                                            style={{
                                                background: isCurrent ? 'var(--vora-accent-soft)' : 'transparent',
                                                borderLeft: `2px solid ${isCurrent ? 'var(--vora-accent-500)' : 'transparent'}`,
                                            }}
                                        >
                                            <div className="w-6 shrink-0 text-center text-xs">
                                                {isCurrent
                                                    ? <svg className="mx-auto h-4 w-4" fill="currentColor" viewBox="0 0 24 24" style={{ color: 'var(--vora-accent-text)' }}><path d="M8 5v14l11-7z" /></svg>
                                                    : <span style={{ color: 'var(--vora-text-disabled)' }}>{idx + 1}</span>}
                                            </div>
                                            <div className="min-w-0 flex-1">
                                                <div
                                                    className="truncate text-sm font-medium"
                                                    style={{ color: isCurrent ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                                                >
                                                    {item.title}
                                                </div>
                                                <div className="truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{item.subtitle}</div>
                                            </div>
                                        </button>
                                    );
                                })}
                            </div>
                        </div>
                    )}

                    {streamError ? (
                        <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 px-4 text-center" style={{ background: 'rgba(0, 0, 0, 0.6)' }}>
                            <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" style={{ color: 'var(--vora-accent-500)' }}>
                                <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                                <line x1="12" y1="9" x2="12" y2="13" />
                                <line x1="12" y1="17" x2="12.01" y2="17" />
                            </svg>
                            <span className="text-xs font-medium" style={{ color: 'var(--vora-text-secondary)' }}>{streamError}</span>
                        </div>
                    ) : isLoading ? (
                        <div className="pointer-events-none absolute inset-0 flex items-center justify-center" style={{ background: 'rgba(0, 0, 0, 0.4)' }}>
                            <svg className="h-12 w-12 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" style={{ color: 'var(--vora-accent-500)' }}>
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                            </svg>
                        </div>
                    ) : null}
                </div>
            ))}
        </div>
    );
}
