import { useCallback, useEffect, useState, useRef, useMemo } from 'react';
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
import RadioNowPlaying from './RadioNowPlaying';
import PodcastNowPlaying from './PodcastNowPlaying';

export default function LiveRadioPlayer() {
    const { currentMedia, isPlaying, isMinimized, volume, togglePlayPause, setMinimized, closePlayer, setVolume, videoRef, playMedia, skipForward, skipBackward, seek, nextTrack, previousTrack, hasNext, hasPrevious, setFullscreen, isFullscreen } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const playerContainerRef = useRef<HTMLDivElement>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [streamError, setStreamError] = useState<string | null>(null);
    const [channels, setChannels] = useState<IptvChannelVM[]>([]);

    const isPodcast = currentMedia?.playbackContextType === 'Podcast';
    const isMusic = currentMedia?.playbackContextType === 'Music';
    const isAudioOnDemand = isPodcast || isMusic;
    const isLiveRadio = currentMedia?.playbackContextType === 'LiveRadio';
    const minimizePlayer = useCallback(() => setMinimized(true), [setMinimized]);

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
        const loadTimeout = setTimeout(() => failStream('This station is not responding. Try another station.'), 20000);
        video.addEventListener('playing', onVideoPlaying);
        video.addEventListener('error', onVideoError);

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

    const playStation = (channel: IptvChannelVM) => {
        if (!currentMedia || channel.id === currentMedia.id) return;
        setIsLoading(true);
        playMedia({
            ...currentMedia,
            id: channel.id,
            title: channel.name,
            subtitle: channel.groupTitle || 'Live Radio',
            posterUrl: channel.logoUrl,
            streamUrl: channel.streamUrl,
            container: 'hls',
            playbackContextType: 'LiveRadio'
        });
    };

    const handleChannelChange = (direction: 'next' | 'prev') => {
        if (!channels.length || !currentMedia) return;
        const currentIndex = channels.findIndex(c => c.id === currentMedia.id);
        if (currentIndex === -1) return;

        let newIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
        if (newIndex >= channels.length) newIndex = 0;
        if (newIndex < 0) newIndex = channels.length - 1;

        playStation(channels[newIndex]);
    };

    if (!currentMedia) return null;

    const hiddenForFullscreen = isFullscreen && isMusic;
    const showBar = isMinimized || isMusic;
    const expandPlayer = () => isMusic ? setFullscreen(true) : setMinimized(false);

    const containerClass = hiddenForFullscreen
        ? 'fixed bottom-0 left-0 right-0 h-0 overflow-hidden pointer-events-none z-[1]'
        : `transition-all duration-300 ease-in-out ${showBar ? 'fixed bottom-0 left-0 right-0 z-[99999] flex h-24 flex-col vora-glass' : 'fixed inset-0 z-[99999]'}`;

    const containerStyle: React.CSSProperties | undefined = hiddenForFullscreen
        ? undefined
        : showBar
            ? { borderTop: '1px solid var(--vora-border-subtle)' }
            : { background: 'var(--vora-bg-canvas)' };

    return (
        <div ref={playerContainerRef} className={containerClass} style={containerStyle}>

            <video ref={videoRef} autoPlay playsInline className="hidden" />

            {!hiddenForFullscreen && (showBar ? (
                <div className="flex h-full w-full items-center justify-between px-6">
                    <div className="flex min-w-0 flex-1 items-center gap-4">
                        <div
                            className="flex h-16 w-16 shrink-0 cursor-pointer items-center justify-center overflow-hidden rounded-md transition-all hover:ring-2"
                            onClick={expandPlayer}
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
                                onClick={expandPlayer}
                                style={{ color: 'var(--vora-text-primary)' }}
                            >
                                {currentMedia.title}
                            </span>
                            <span className="truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                                {currentMedia.subtitle || 'Live Radio'}
                            </span>
                        </div>
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
                        {isAudioOnDemand && <SkipButton seconds={10} direction="back" size="sm" onClick={() => skipBackward(10)} />}
                        <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} size="sm" />
                        {isAudioOnDemand && <SkipButton seconds={30} direction="forward" size="sm" onClick={() => skipForward(30)} />}
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
                        <MaximizeButton onClick={expandPlayer} />
                        <CloseButton onClick={closePlayer} />
                    </div>
                </div>
            ) : isLiveRadio ? (
                <RadioNowPlaying
                    station={currentMedia}
                    stations={channels}
                    isPlaying={isPlaying}
                    isLoading={isLoading}
                    streamError={streamError}
                    volume={volume}
                    onVolumeChange={setVolume}
                    onTogglePlay={togglePlayPause}
                    onPreviousStation={() => handleChannelChange('prev')}
                    onNextStation={() => handleChannelChange('next')}
                    onSelectStation={playStation}
                    onMinimize={minimizePlayer}
                    onClose={closePlayer}
                />
            ) : isPodcast ? (
                <PodcastNowPlaying
                    episode={currentMedia}
                    isPlaying={isPlaying}
                    currentTime={currentTime}
                    duration={duration}
                    volume={volume}
                    onVolumeChange={setVolume}
                    onTogglePlay={togglePlayPause}
                    onSeek={seek}
                    onSkipBack={skipBackward}
                    onSkipForward={skipForward}
                    onMinimize={minimizePlayer}
                    onClose={closePlayer}
                />
            ) : null)}
        </div>
    );
}
