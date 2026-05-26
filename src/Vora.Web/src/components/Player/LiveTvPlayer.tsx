import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { useEffect, useState, useRef, useMemo } from 'react';
import type Hls from 'hls.js';
import { loadHls } from '../../utils/loadHls';
import { type IptvChannelVM } from '../../api/Iptv/iptvAdminService';
import { iptvClientService, type IptvProgramDto } from '../../api/Iptv/iptvClientService';
import { dvrService, type IptvRecordingSessionVM } from '../../api/Iptv/dvrService';
import { timeshiftService } from '../../api/Iptv/timeshiftService';
import { passthroughService } from '../../api/Iptv/passthroughService';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys, decodeJwtPayload, getProfileIdFromToken } from '../../utils/storageKeys';
import LiveTvGuide from '../../pages/Client/LiveTv/LiveTvGuide';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { useAutoHideControls } from './Controls/useAutoHideControls';
import { useFullscreen } from './Controls/useFullscreen';
import { PlayPauseButton, SkipButton, VolumeControl, FullscreenButton, MaximizeButton, CloseButton } from './Controls/PlayerButtons';
import LiveTvInfoPanel from './Panels/LiveTvInfoPanel';
import LiveTvRecordModal from './Panels/LiveTvRecordModal';
import { useCallback } from 'react';
import { useDialog } from '../../dialogs';
import { userService } from '../../api/Users/userService';

export default function LiveTvPlayer() {
    const dialog = useDialog();
    const { currentMedia, isPlaying, isMinimized, volume, togglePlayPause, setMinimized, closePlayer, setVolume, videoRef, playMedia, seek, skipForward, skipBackward } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const initialPermissions = useMemo(() => {
        const token = localStorage.getItem(StorageKeys.profileToken);
        const isServerAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
        if (!token) return { canTimeshift: isServerAdmin, canRecord: isServerAdmin };
        try {
            const payload = decodeJwtPayload(token) ?? {};
            const truthy = (v: unknown) => v === true || v === 'True' || v === 'true' || v === '1' || v === 1;
            return {
                canTimeshift: isServerAdmin || truthy(payload.canTimeshiftIptv),
                canRecord: isServerAdmin || truthy(payload.canRecordLiveTv),
            };
        } catch {
            return { canTimeshift: isServerAdmin, canRecord: isServerAdmin };
        }
    }, []);
    const [canTimeshift, setCanTimeshift] = useState(initialPermissions.canTimeshift);
    const [canRecord, setCanRecord] = useState(initialPermissions.canRecord);

    useEffect(() => {
        const userId = localStorage.getItem(StorageKeys.userId);
        if (!userId) return;
        userService.getUserAccount(userId, serverVault.getActiveServerId() || undefined)
            .then(u => {
                setCanTimeshift(!!u.canTimeshiftIptv || !!u.isAdmin);
                setCanRecord(!!u.canRecordLiveTv || !!u.isAdmin);
            })
            .catch(err => console.warn('Failed to refresh live TV permissions', err));
    }, []);

    const playerContainerRef = useRef<HTMLDivElement>(null);
    const [showInfo, setShowInfo] = useState(false);
    const [showOverlayGuide, setShowOverlayGuide] = useState(false);
    const [hoveredGuideItem, setHoveredGuideItem] = useState<{ channel: IptvChannelVM, program: IptvProgramDto | null } | null>(null);

    const [isVideoLoading, setIsVideoLoading] = useState(true);
    const [ccAvailable, setCcAvailable] = useState(false);
    const [ccEnabled, setCcEnabled] = useState(false);
    const ccEnabledRef = useRef(ccEnabled);
    const [channels, setChannels] = useState<IptvChannelVM[]>([]);
    const [guideData, setGuideData] = useState<Record<string, IptvProgramDto[]>>({});
    const [recordingSessions, setRecordingSessions] = useState<IptvRecordingSessionVM[]>([]); // <-- NEW
    const [showRecordModal, setShowRecordModal] = useState(false);
    const [recordRetention, setRecordRetention] = useState(0);

    useEffect(() => { ccEnabledRef.current = ccEnabled; }, [ccEnabled]);

    useEffect(() => {
        if (currentMedia?.playbackContextType !== 'LiveTv') return;

        const loadChannelsAndGuide = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;
                const userId = localStorage.getItem(StorageKeys.userId) || activeProfileId;
                const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';

                const allProviders = await iptvClientService.getPlaylists(userId, activeProfileId, activeServer.id);
                let enabledProviderIds: string[] = [];
                let hasSavedSettings = false;
                const savedIptv = localStorage.getItem(StorageKeys.iptvPrefs(activeProfileId, deviceId));

                if (savedIptv && savedIptv !== "[]" && savedIptv !== "") {
                    hasSavedSettings = true;
                    const raw = JSON.parse(savedIptv);
                    enabledProviderIds = Array.isArray(raw) ? raw : raw.enabledProviders || [];
                    enabledProviderIds = enabledProviderIds.filter((id: string) => allProviders.some(p => p.id === id));
                }

                if (!hasSavedSettings && enabledProviderIds.length === 0 && allProviders.length > 0) {
                    enabledProviderIds = allProviders.map(p => p.id);
                }

                const activeChannels = allProviders.filter(p => enabledProviderIds.includes(p.id)).flatMap(p => p.channels || []);
                setChannels(activeChannels);

                const now = new Date();
                const start = new Date(now.getTime() - 2 * 60 * 60 * 1000);
                const end = new Date(now.getTime() + 4 * 60 * 60 * 1000);

                const channelIds = activeChannels.map(c => c.externalChannelId);
                const guide = await iptvClientService.getGuide(userId, activeProfileId, channelIds, start.toISOString(), end.toISOString(), activeServer.id);

                try {
                    const sessions = await dvrService.getRecordingSessions(activeProfileId, activeServer.id);
                    setRecordingSessions(sessions);
                } catch (e) { console.error(e); }

                const normalizedGuide: Record<string, IptvProgramDto[]> = {};
                for (const [key, value] of Object.entries(guide)) normalizedGuide[key.toLowerCase()] = value;

                setGuideData(normalizedGuide);
            } catch (error) {
                console.error("Failed to load channel data for player", error);
            }
        };
        loadChannelsAndGuide();
    }, [currentMedia?.playbackContextType]);

    useSignalREvent("DvrSessionsUpdated", useCallback(() => {
        const fetchFreshSessions = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;

                const sessions = await dvrService.getRecordingSessions(activeProfileId, activeServer.id);
                setRecordingSessions(sessions);
            } catch (e) {
                console.error("SignalR: Failed to refresh DVR sessions", e);
            }
        };
        fetchFreshSessions();
    }, []));

    useEffect(() => {
        const video = videoRef.current;
        if (!video || !currentMedia?.id) return;

        let hls: Hls | null = null;
        let isMounted = true;
        const loadTimer = setTimeout(() => setIsVideoLoading(true), 0);

        const attachDirectStream = async () => {
            const activeServer = serverVault.getActiveServer();
            let passthroughUrl: string;
            try {
                const data = await passthroughService.startPassthrough(currentMedia.id, activeServer?.id);
                if (!isMounted) return;
                passthroughUrl = `${import.meta.env.VITE_API_URL || ''}${data.url}`;
            } catch (err) {
                console.error("Failed to start IPTV passthrough:", err);
                setIsVideoLoading(false);
                return;
            }

            const HlsClass = await loadHls();
            if (!isMounted) return;

            if (HlsClass.isSupported()) {
                hls = new HlsClass();
                hls.loadSource(passthroughUrl);
                hls.attachMedia(video);
                hls.on(HlsClass.Events.MANIFEST_PARSED, () => {
                    if (isMounted) {
                        video.play().catch(e => console.error(e));
                        setIsVideoLoading(false);
                    }
                });
                hls.on(HlsClass.Events.SUBTITLE_TRACKS_UPDATED, (_, data) => {
                    setCcAvailable(data.subtitleTracks.length > 0);
                });
            } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                video.src = passthroughUrl;
                video.play().catch(e => console.error(e));
                setIsVideoLoading(false);
            }
        };

        const initializeStream = async () => {
            if (!canTimeshift) {
                await attachDirectStream();
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
                        liveSyncDurationCount: 3,
                        manifestLoadingMaxRetry: 3,
                        fragLoadingMaxRetry: 3
                    });

                    hls.loadSource(finalUrl);
                    hls.attachMedia(video);

                    hls.on(HlsClass.Events.MANIFEST_PARSED, () => {
                        if (isMounted) {
                            video.play().catch(e => console.error("Auto-play blocked:", e));
                            setIsVideoLoading(false);
                        }
                    });

                    hls.on(HlsClass.Events.SUBTITLE_TRACKS_UPDATED, (_, data) => {
                        setCcAvailable(data.subtitleTracks.length > 0);
                    });

                    hls.on(HlsClass.Events.ERROR, (_, data) => {
                        if (data.fatal) {
                            console.error("Fatal Stream Error:", data);
                            hls?.destroy();
                            setIsVideoLoading(false);
                        }
                    });
                } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                    video.src = finalUrl;
                    video.play().catch(e => console.error(e));
                    setIsVideoLoading(false);
                }
            } catch (err) {
                if (!isMounted) return;
                console.error("Timeshift failed, falling back to passthrough:", err);
                await attachDirectStream();
            }
        };

        initializeStream();

        const pingInterval = canTimeshift ? setInterval(() => {
            const activeServer = serverVault.getActiveServer();
            timeshiftService.pingTimeshift(activeServer?.id).catch(() => { });
        }, 30000) : null;

        return () => {
            isMounted = false;
            if (pingInterval) clearInterval(pingInterval);
            clearTimeout(loadTimer);
            if (hls) hls.destroy();

            video.removeAttribute('src');
            video.load();

            if (canTimeshift) {
                const activeServer = serverVault.getActiveServer();
                timeshiftService.stopTimeshift(activeServer?.id).catch(() => { });
            }
        };
    }, [currentMedia?.id, currentMedia?.streamUrl, videoRef, canTimeshift]);

    const handleChannelChange = (direction: 'next' | 'prev') => {
        if (!channels.length || !currentMedia) return;
        const currentIndex = channels.findIndex(c => c.id === currentMedia.id);
        if (currentIndex === -1) return;

        let newIndex = direction === 'next' ? currentIndex + 1 : currentIndex - 1;
        if (newIndex >= channels.length) newIndex = 0;
        if (newIndex < 0) newIndex = channels.length - 1;

        const nextChannel = channels[newIndex];
        setIsVideoLoading(true);
        playMedia({
            ...currentMedia, id: nextChannel.id, title: nextChannel.name, subtitle: 'Live TV',
            posterUrl: nextChannel.logoUrl, streamUrl: nextChannel.streamUrl,
        });
    };

    useEffect(() => {
        const video = videoRef.current;
        if (!video) return;
        const tracks = video.textTracks;
        const desired: TextTrackMode = ccEnabled ? 'showing' : 'hidden';

        const enforce = () => {
            for (let i = 0; i < tracks.length; i++) {
                const t = tracks[i];
                if ((t.kind === 'captions' || t.kind === 'subtitles') && t.mode !== desired) {
                    t.mode = desired;
                }
            }
        };

        enforce();
        tracks.addEventListener('addtrack', enforce);
        tracks.addEventListener('change', enforce);
        return () => {
            tracks.removeEventListener('addtrack', enforce);
            tracks.removeEventListener('change', enforce);
        };
    }, [ccEnabled, currentMedia?.id, videoRef]);

    const showControls = useAutoHideControls({
        isMinimized,
        isPlaying,
        keepVisibleWhen: showInfo || showOverlayGuide
    });

    const toggleCc = () => {
        setCcEnabled(prev => !prev);
    };

    const formatTime = (dateStr: string) => {
        if (!dateStr) return '';
        const date = new Date(dateStr.endsWith('Z') ? dateStr : dateStr + 'Z');
        return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    };

    const currentChannel = useMemo(() => channels.find(c => c.id === currentMedia?.id), [channels, currentMedia?.id]);

    const activeProgram = useMemo(() => {
        const internalNow = new Date();
        const rawPrograms = currentChannel ? guideData[(currentChannel.externalChannelId || '').toLowerCase()] || [] : [];
        return rawPrograms.find(p => {
            const s = new Date(p.startTime.endsWith('Z') ? p.startTime : p.startTime + 'Z');
            const e = new Date(p.endTime.endsWith('Z') ? p.endTime : p.endTime + 'Z');
            return s <= internalNow && e > internalNow;
        });
    }, [currentChannel, guideData]);

    const displayChannel = hoveredGuideItem ? hoveredGuideItem.channel : currentChannel;
    const displayProgram = hoveredGuideItem ? hoveredGuideItem.program : activeProgram;

    const isActivelyRecording = useMemo(() => {
        if (!activeProgram || !currentChannel) return false;
        return recordingSessions.some(s => {
            if (s.status !== 'Pending' && s.status !== 'Recording') return false;

            if (s.externalProgramId && s.externalProgramId === activeProgram.id) return true;

            if (s.title !== activeProgram.title || s.schedule?.channel?.name !== currentChannel.name) return false;

            const sStart = new Date(s.startTime).getTime();
            const sEnd = new Date(s.endTime).getTime();
            const pStart = new Date(activeProgram.startTime.endsWith('Z') ? activeProgram.startTime : activeProgram.startTime + 'Z').getTime();
            const pEnd = new Date(activeProgram.endTime.endsWith('Z') ? activeProgram.endTime : activeProgram.endTime + 'Z').getTime();

            const overlapStart = Math.max(sStart, pStart);
            const overlapEnd = Math.min(sEnd, pEnd);

            return (overlapEnd - overlapStart) > 360000;
        });
    }, [activeProgram, currentChannel, recordingSessions]);

    const toggleFullScreen = useFullscreen(playerContainerRef);

    if (!currentMedia) return null;

    return (
        <div
            ref={playerContainerRef}
            className={`transition-all duration-300 ease-in-out ${isMinimized ? 'fixed bottom-0 left-0 right-0 z-[99999] flex h-24 flex-col vora-glass' : 'fixed inset-0 z-[99999]'}`}
            style={isMinimized
                ? { borderTop: '1px solid var(--vora-border-subtle)' }
                : { background: 'var(--vora-bg-canvas)' }}
        >

            <video
                key={currentMedia.sessionId || currentMedia.id}
                ref={videoRef}
                autoPlay playsInline
                onClick={() => { if (isMinimized) setMinimized(false); }}
                onWaiting={() => setIsVideoLoading(true)}
                onPlaying={() => setIsVideoLoading(false)}
                onCanPlay={() => setIsVideoLoading(false)}
                className={`bg-black transition-all duration-500 ease-in-out
                    ${isMinimized ? 'absolute bottom-4 left-6 z-20 h-16 w-16 cursor-pointer rounded-md object-cover shadow-lg hover:ring-2 hover:ring-[var(--vora-accent-500)]'
                        : showOverlayGuide ? 'absolute left-0 top-0 z-[9001] h-[40vh] w-1/2 object-contain shadow-2xl lg:w-[40%]'
                            : 'absolute left-0 top-0 z-0 h-[100vh] w-full object-contain'}`}
            />

            {isVideoLoading && !isMinimized && (
                <div className={`pointer-events-none absolute z-[9002] flex items-center justify-center transition-all duration-500 ease-in-out
                    ${showOverlayGuide ? 'left-0 top-0 h-[40vh] w-1/2 lg:w-[40%]' : 'inset-0'}`}
                    style={{ background: 'rgba(0, 0, 0, 0.6)' }}>
                    <svg className="h-12 w-12 animate-spin drop-shadow-lg" style={{ color: 'var(--vora-accent-500)' }} xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                </div>
            )}

            {isMinimized && (
                <div className="z-10 flex h-full w-full flex-1 items-center justify-between px-6">
                    <div className="flex w-1/4 items-center gap-4 pl-24">
                        <div className="flex flex-col overflow-hidden">
                            <span
                                className="cursor-pointer truncate font-semibold hover:underline"
                                onClick={() => setMinimized(false)}
                                style={{ color: 'var(--vora-text-primary)' }}
                            >
                                {currentMedia.title}
                            </span>
                            <span className="truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                                {activeProgram?.title || currentMedia.subtitle}
                            </span>
                        </div>
                    </div>

                    <div className="flex max-w-2xl flex-1 flex-col items-center justify-center px-8">
                        <div className="mb-2 flex items-center gap-4">
                            <button
                                type="button"
                                onClick={() => handleChannelChange('prev')}
                                title="Previous channel"
                                className="cursor-pointer transition-colors"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </button>
                            {canTimeshift && <SkipButton seconds={10} direction="back" size="sm" onClick={() => skipBackward(10)} />}
                            <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} size="sm" />
                            {canTimeshift && <SkipButton seconds={30} direction="forward" size="sm" onClick={() => skipForward(30)} />}
                            <button
                                type="button"
                                onClick={() => handleChannelChange('next')}
                                title="Next channel"
                                className="cursor-pointer transition-colors"
                                style={{ color: 'var(--vora-text-muted)' }}
                            >
                                <svg className="h-5 w-5 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                            </button>
                        </div>
                        <div className="flex items-center gap-2">
                            <span className="h-2 w-2 animate-pulse rounded-full" style={{ background: 'var(--vora-danger-500)' }} />
                            <span className="text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-danger-text)' }}>Live</span>

                            {isActivelyRecording && (
                                <span
                                    className="ml-2 rounded px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider"
                                    style={{ background: 'var(--vora-danger-soft)', border: '1px solid var(--vora-danger-500)', color: 'var(--vora-danger-text)' }}
                                >
                                    Recording
                                </span>
                            )}
                        </div>
                    </div>

                    <div className="flex w-1/4 items-center justify-end gap-4">
                        <MaximizeButton onClick={() => setMinimized(false)} />
                        <CloseButton onClick={closePlayer} />
                    </div>
                </div>
            )}

            {!isMinimized && !showOverlayGuide && (
                <div
                    className={`absolute inset-0 z-30 flex flex-col justify-between transition-opacity duration-500 ${showControls || showInfo ? 'opacity-100' : 'pointer-events-none opacity-0'}`}
                    style={{
                        background: showControls || showInfo
                            ? 'linear-gradient(180deg, rgba(0, 0, 0, 0.7) 0%, transparent 18%, transparent 70%, rgba(0, 0, 0, 0.85) 100%)'
                            : 'transparent',
                    }}
                >

                    <div className="pointer-events-auto flex items-start justify-between p-8">
                        <button
                            type="button"
                            onClick={closePlayer}
                            aria-label="Close player"
                            className="flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                            style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
                        >
                            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
                        </button>

                        <div className="flex flex-col items-center text-center">
                            <h1 className="m-0 text-2xl font-semibold drop-shadow-lg" style={{ color: '#fafafa', letterSpacing: '-0.01em' }}>{currentMedia.title}</h1>
                            <p className="m-0 mt-1 text-lg drop-shadow-md" style={{ color: 'rgba(255, 255, 255, 0.82)' }}>{activeProgram?.title || currentMedia.subtitle}</p>
                            <div className="mt-2 inline-flex items-center gap-2">
                                <span className="h-1.5 w-1.5 animate-pulse rounded-full" style={{ background: 'var(--vora-danger-500)' }} />
                                <span className="text-[11px] font-bold uppercase tracking-widest" style={{ color: 'var(--vora-danger-text)' }}>Live</span>
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

                    {/* BOTTOM CONTROLS & SCRUBBER */}
                    <div className="relative w-full pointer-events-auto">

                        <div className="mx-auto flex w-full max-w-screen-2xl items-center justify-between p-8 pb-12" style={{ color: '#fafafa' }}>
                            <div className="flex w-1/3 items-center gap-6"></div>

                            <div className="flex w-1/3 items-center justify-center gap-6">
                                <button
                                    type="button"
                                    onClick={() => handleChannelChange('prev')}
                                    title="Previous channel"
                                    className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/10"
                                >
                                    <svg className="h-8 w-8 fill-current" viewBox="0 0 24 24"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                                </button>
                                {canTimeshift && <SkipButton seconds={10} direction="back" onClick={() => skipBackward(10)} />}
                                <PlayPauseButton isPlaying={isPlaying} onClick={togglePlayPause} />
                                {canTimeshift && <SkipButton seconds={30} direction="forward" onClick={() => skipForward(30)} />}
                                <button
                                    type="button"
                                    onClick={() => handleChannelChange('next')}
                                    title="Next channel"
                                    className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/10"
                                >
                                    <svg className="h-8 w-8 fill-current" viewBox="0 0 24 24"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" /></svg>
                                </button>
                            </div>

                            <div className="flex w-1/3 items-center justify-end gap-3">
                                {canRecord && isActivelyRecording ? (
                                    <div
                                        className="inline-flex cursor-default items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-bold uppercase tracking-wider backdrop-blur-md"
                                        style={{
                                            background: 'var(--vora-danger-soft)',
                                            border: '1px solid var(--vora-danger-500)',
                                            color: 'var(--vora-danger-text)',
                                        }}
                                    >
                                        <span className="h-1.5 w-1.5 animate-pulse rounded-full" style={{ background: 'var(--vora-danger-500)' }} />
                                        Recording
                                    </div>
                                ) : canRecord && activeProgram ? (
                                    <button
                                        type="button"
                                        onClick={() => setShowRecordModal(true)}
                                        className="inline-flex cursor-pointer items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-semibold backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                        style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }}
                                    >
                                        <span className="h-2 w-2 rounded-full" style={{ background: 'var(--vora-danger-500)' }} />
                                        Record
                                    </button>
                                ) : null}

                                {ccAvailable && (
                                    <button
                                        type="button"
                                        onClick={toggleCc}
                                        className="inline-flex cursor-pointer items-center justify-center rounded-md px-2.5 py-1 text-xs font-bold transition-colors backdrop-blur-md"
                                        style={ccEnabled
                                            ? { background: 'var(--vora-accent-soft)', border: '1px solid var(--vora-accent-soft-hover)', color: 'var(--vora-accent-text)' }
                                            : { background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }
                                        }
                                    >
                                        CC
                                    </button>
                                )}

                                <button
                                    type="button"
                                    onClick={() => setShowOverlayGuide(true)}
                                    className="inline-flex cursor-pointer items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-semibold backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                    style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }}
                                >
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 6h16M4 10h16M4 14h16M4 18h16" /></svg>
                                    Guide
                                </button>

                                <button
                                    type="button"
                                    onClick={() => setShowInfo(!showInfo)}
                                    aria-label="Info"
                                    className="cursor-pointer rounded-full p-2 transition-colors hover:bg-white/10"
                                    style={{ color: showInfo ? 'var(--vora-accent-text)' : '#fafafa' }}
                                >
                                    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" /></svg>
                                </button>

                                <VolumeControl value={volume} onChange={setVolume} />

                                <FullscreenButton onClick={toggleFullScreen} />
                            </div>
                        </div>

                        {canTimeshift && (
                            <div
                                className="group absolute bottom-0 left-0 right-0 h-1.5 cursor-pointer"
                                style={{ background: 'rgba(255, 255, 255, 0.14)' }}
                            >
                                <div className="relative h-full" style={{ width: `${(currentTime / (duration || 1)) * 100}%`, background: 'var(--vora-accent-500)' }}>
                                    <div className="absolute right-0 top-1/2 h-3 w-3 -translate-y-1/2 translate-x-1/2 transform rounded-full opacity-0 shadow group-hover:opacity-100" style={{ background: 'var(--vora-accent-500)' }} />
                                </div>
                                <input
                                    type="range" min={0} max={duration || 100} value={currentTime}
                                    onChange={e => seek(Number(e.target.value))}
                                    aria-label="Playback position"
                                    className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
                                />
                            </div>
                        )}
                    </div>
                </div>
            )}

            {showInfo && !isMinimized && (
                <LiveTvInfoPanel activeProgram={activeProgram} streamUrl={currentMedia.streamUrl} formatTime={formatTime} />
            )}

            {showOverlayGuide && !isMinimized && (
                <div
                    className="animate-in fade-in absolute inset-0 z-[9000] flex flex-col duration-300"
                    style={{ background: 'var(--vora-bg-canvas)' }}
                >
                    <div className="flex h-[40vh] shrink-0" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                        <div className="relative w-1/2 bg-black lg:w-[40%]" />
                        <div className="relative flex-1 overflow-y-auto p-8" style={{ background: 'var(--vora-bg-surface)' }}>
                            <button
                                type="button"
                                onClick={() => { setShowOverlayGuide(false); setHoveredGuideItem(null); }}
                                className="absolute right-6 top-6 inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold transition-colors hover:bg-white/5"
                                style={{ background: 'rgba(255, 255, 255, 0.04)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-secondary)' }}
                            >
                                Close
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                            </button>
                            <div className="mb-4 flex items-center gap-4">
                                <div
                                    className="relative flex h-16 w-16 items-center justify-center overflow-hidden rounded-full"
                                    style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                                >
                                    {displayChannel?.logoUrl && <img src={displayChannel.logoUrl} alt="" className="max-h-[80%] max-w-[80%] object-contain" />}
                                </div>
                                <div>
                                    <h2 className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{displayChannel?.name || currentMedia.title}</h2>
                                    {displayChannel?.groupTitle && (
                                        <span className="text-[10px] font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>
                                            {displayChannel.groupTitle.replace(/;/g, ' • ')}
                                        </span>
                                    )}
                                </div>
                            </div>
                            <h3 className="m-0 mb-2 text-xl font-semibold" style={{ color: 'var(--vora-text-secondary)' }}>{displayProgram?.title || 'Live TV'}</h3>
                            <p className="m-0 max-w-3xl text-sm leading-relaxed" style={{ color: 'var(--vora-text-muted)' }}>{displayProgram?.description}</p>
                        </div>
                    </div>
                    <div className="h-[60vh] w-full flex-1">
                        <LiveTvGuide
                            isEmbedded={true}
                            currentPlayingChannelId={currentMedia.id}
                            onHoverProgram={(channel, program) => setHoveredGuideItem({ channel, program })}
                            onPlayChannel={(channel, program) => {
                                setIsVideoLoading(true);
                                playMedia({
                                    id: channel.id, title: channel.name, subtitle: program ? program.title : 'Live TV',
                                    posterUrl: channel.logoUrl, streamUrl: channel.streamUrl, serverId: currentMedia.serverId, container: 'hls', playbackContextType: 'LiveTv',
                                });
                                setShowOverlayGuide(false);
                            }}
                        />
                    </div>
                </div>
            )}

            {showRecordModal && currentChannel && activeProgram && (
                <LiveTvRecordModal
                    channel={currentChannel}
                    program={activeProgram}
                    formatTime={formatTime}
                    recordRetention={recordRetention}
                    onChangeRetention={setRecordRetention}
                    onRecordEpisode={async () => {
                        const activeServer = serverVault.getActiveServer();
                        const profileId = getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken));
                        if (!profileId) { await dialog.alert("No active profile. Re-select a profile to schedule recordings."); return; }
                        await dvrService.scheduleRecording(profileId, currentChannel.id, activeProgram.title, activeProgram.id, false, 0, activeServer?.id);
                        const newSessions = await dvrService.getRecordingSessions(profileId, activeServer?.id);
                        setRecordingSessions(newSessions);
                        setShowRecordModal(false);
                        await dialog.alert("Episode scheduled for recording.");
                    }}
                    onRecordSeries={async () => {
                        const activeServer = serverVault.getActiveServer();
                        const profileId = getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken));
                        if (!profileId) { await dialog.alert("No active profile. Re-select a profile to schedule recordings."); return; }
                        await dvrService.scheduleRecording(profileId, currentChannel.id, activeProgram.title, activeProgram.id, true, recordRetention, activeServer?.id);
                        const newSessions = await dvrService.getRecordingSessions(profileId, activeServer?.id);
                        setRecordingSessions(newSessions);
                        setShowRecordModal(false);
                        await dialog.alert("Series scheduled for recording.");
                    }}
                    onCancel={() => setShowRecordModal(false)}
                />
            )}
        </div>
    );
}