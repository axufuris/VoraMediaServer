import { createContext, useContext, useState, useRef, useEffect, useCallback, type ReactNode } from 'react';
import { streamingService } from '../api/Streaming/streamingService';
import { useSignalREvent } from '../hooks/useSignalREvent';
import { musicService, type RadioSeed } from '../api/Music/musicService';
import { serverVault } from '../utils/serverVault';
import { audioQualityStore, crossfadeStore, eqPresetStore, EQ_PRESETS } from '../utils/audioQuality';
import { useDialog } from '../dialogs';

export interface PlayableMedia {
    id: string;
    title: string;
    subtitle?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    streamUrl: string;
    serverId?: string;
    sessionId?: string;
    startPosition?: number;
    resolution?: string;
    hdrType?: string;
    audioChannels?: number;
    videoTrackId?: string;
    audioTrackId?: string;
    subtitleTrackId?: string | null;
    strategy?: string;
    videoStrategy?: string;
    audioStrategy?: string;
    subtitleStrategy?: string | null;
    videoCodec?: string;
    audioCodec?: string;
    targetAudioChannels?: number;
    container?: string;
    bandwidthKbps?: number;
    playbackContextType?: string;
    playbackContextId?: string;
    commercialMarkers?: { start: number, end: number }[]; // <-- ADDED
}

export type RepeatMode = 'off' | 'all' | 'one';

interface PlayerContextType {
    currentMedia: PlayableMedia | null;
    isPlaying: boolean;
    isMinimized: boolean;
    currentTime: number;
    duration: number;
    volume: number;
    sessionId: string | null;
    playMedia: (media: PlayableMedia) => void;
    playQueue: (items: PlayableMedia[], startIndex?: number) => void;
    addToQueue: (items: PlayableMedia[]) => void;
    playNext: (items: PlayableMedia[]) => void;
    nextTrack: () => void;
    previousTrack: () => void;
    jumpToQueueIndex: (idx: number) => void;
    hasNext: boolean;
    hasPrevious: boolean;
    queue: PlayableMedia[];
    queueIndex: number;
    isShuffled: boolean;
    toggleShuffle: () => void;
    repeatMode: RepeatMode;
    cycleRepeatMode: () => void;
    togglePlayPause: () => void;
    seek: (time: number) => void;
    skipForward: (seconds: number) => void;
    skipBackward: (seconds: number) => void;
    setMinimized: (min: boolean) => void;
    isFullscreen: boolean;
    toggleFullscreen: () => void;
    setFullscreen: (full: boolean) => void;
    closePlayer: () => void;
    setVolume: (vol: number) => void;
    changeStreams: (videoTrackId: string, audioTrackId: string, subtitleTrackId: string) => Promise<void>;
    videoRef: React.RefObject<HTMLVideoElement | null>;
    radioSeed: RadioSeed | null;
    radioLabel: string | null;
    startRadio: (seed: RadioSeed, label: string, items: PlayableMedia[]) => void;
}

const PlayerContext = createContext<PlayerContextType | undefined>(undefined);

export function PlayerProvider({ children }: { children: ReactNode }) {
    const dialog = useDialog();
    const [currentMedia, setCurrentMedia] = useState<PlayableMedia | null>(null);
    const [isPlaying, setIsPlaying] = useState(false);
    const [isMinimized, setIsMinimized] = useState(false);
    const [currentTime, setCurrentTime] = useState(0);
    const [duration, setDuration] = useState(0);
    const [volume, setVolumeState] = useState(1);
    const [sessionId, setSessionId] = useState<string | null>(null);

    const [adminMessage, setAdminMessage] = useState<string | null>(null);
    const videoRef = useRef<HTMLVideoElement>(null);

    const [queue, setQueue] = useState<PlayableMedia[]>([]);
    const [queueIndex, setQueueIndex] = useState(0);
    const queueRef = useRef<PlayableMedia[]>([]);
    const queueIndexRef = useRef(0);
    useEffect(() => { queueRef.current = queue; }, [queue]);
    useEffect(() => { queueIndexRef.current = queueIndex; }, [queueIndex]);

    const [isFullscreen, setIsFullscreen] = useState(false);
    useEffect(() => {
        if (!currentMedia || currentMedia.playbackContextType !== 'Music') {
            setIsFullscreen(false);
        } else {
            setIsMinimized(true);
        }
    }, [currentMedia]);
    const [isShuffled, setIsShuffled] = useState(false);
    const [repeatMode, setRepeatMode] = useState<RepeatMode>('off');
    const repeatModeRef = useRef<RepeatMode>('off');
    useEffect(() => { repeatModeRef.current = repeatMode; }, [repeatMode]);
    const linearQueueRef = useRef<PlayableMedia[]>([]);

    const recordedPlayTrackIdRef = useRef<string | null>(null);
    const currentServerIdRef = useRef<string | undefined>(undefined);
    useEffect(() => {
        currentServerIdRef.current = currentMedia?.serverId;
    }, [currentMedia?.serverId]);
    useEffect(() => {
        recordedPlayTrackIdRef.current = null;
        if (currentMedia?.playbackContextType === 'Music' && currentMedia.id) {
            musicService.updateNowPlaying(currentMedia.id, currentMedia.serverId).catch(() => { /* ignore */ });
        }
    }, [currentMedia?.id, currentMedia?.serverId, currentMedia?.playbackContextType]);

    useEffect(() => {
        if (currentMedia?.playbackContextType !== 'Music' || !currentMedia.id) return;
        if (!isPlaying) return;

        const sendBeat = () => {
            const video = videoRef.current;
            musicService.playbackHeartbeat({
                trackId: currentMedia.id,
                trackTitle: currentMedia.title,
                artist: currentMedia.subtitle?.split(' — ')[0] ?? undefined,
                albumTitle: currentMedia.subtitle?.split(' — ')[1] ?? undefined,
                albumArtworkUrl: currentMedia.posterUrl,
                durationSeconds: video?.duration && isFinite(video.duration) ? Math.floor(video.duration) : undefined,
                currentTimeSeconds: video?.currentTime
            }, currentMedia.serverId);
        };

        sendBeat();
        const interval = setInterval(sendBeat, 10000);
        return () => clearInterval(interval);
    }, [currentMedia?.id, currentMedia?.serverId, currentMedia?.playbackContextType, currentMedia?.title, currentMedia?.subtitle, currentMedia?.posterUrl, isPlaying]);

    useEffect(() => {
        return () => {
            if (!localStorage.getItem('profile_token') && !localStorage.getItem('account_token')) return;
            musicService.playbackStop(currentServerIdRef.current).catch(() => { /* ignore */ });
        };
    }, []);

    const [radioSeed, setRadioSeed] = useState<RadioSeed | null>(null);
    const [radioLabel, setRadioLabel] = useState<string | null>(null);
    const radioExtendingRef = useRef(false);

    const preloadAudioRef = useRef<HTMLAudioElement | null>(null);
    const preloadedUrlRef = useRef<string | null>(null);

    const audioContextRef = useRef<AudioContext | null>(null);
    const audioSourceRef = useRef<MediaElementAudioSourceNode | null>(null);
    const audioGainRef = useRef<GainNode | null>(null);
    const audioEqNodesRef = useRef<BiquadFilterNode[]>([]);
    const fadingOutRef = useRef(false);

    const ensureAudioGraph = useCallback(() => {
        const video = videoRef.current;
        if (!video) return;
        if (audioContextRef.current) return;
        try {
            const Ctx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
            if (!Ctx) return;
            const ctx = new Ctx();
            const source = ctx.createMediaElementSource(video);
            const gain = ctx.createGain();
            gain.gain.value = 1;
            source.connect(gain);
            gain.connect(ctx.destination);
            audioContextRef.current = ctx;
            audioSourceRef.current = source;
            audioGainRef.current = gain;
            applyEqPreset(eqPresetStore.get());
        } catch (err) {
            console.warn('AudioContext setup failed', err);
        }
    }, []);

    const applyEqPreset = useCallback((preset: ReturnType<typeof eqPresetStore.get>) => {
        const ctx = audioContextRef.current;
        const source = audioSourceRef.current;
        const gain = audioGainRef.current;
        if (!ctx || !source || !gain) return;
        try { source.disconnect(); } catch { /* ignore */ }
        for (const n of audioEqNodesRef.current) { try { n.disconnect(); } catch { /* ignore */ } }
        audioEqNodesRef.current = [];

        const bands = EQ_PRESETS[preset] ?? [];
        if (bands.length === 0) {
            source.connect(gain);
            return;
        }
        const filters = bands.map(b => {
            const f = ctx.createBiquadFilter();
            f.type = b.type;
            f.frequency.value = b.freq;
            f.gain.value = b.gain;
            f.Q.value = b.q;
            return f;
        });
        let prev: AudioNode = source;
        for (const f of filters) {
            prev.connect(f);
            prev = f;
        }
        prev.connect(gain);
        audioEqNodesRef.current = filters;
    }, []);

    useEffect(() => {
        if (currentMedia?.playbackContextType !== 'Music') return;
        let cancelled = false;
        const attempt = (retry: number) => {
            if (cancelled) return;
            if (!videoRef.current && retry < 20) {
                setTimeout(() => attempt(retry + 1), 100);
                return;
            }
            ensureAudioGraph();
            const ctx = audioContextRef.current;
            if (ctx && ctx.state === 'suspended') {
                ctx.resume().catch(() => { /* ignore */ });
            }
        };
        attempt(0);
        return () => { cancelled = true; };
    }, [currentMedia?.id, currentMedia?.playbackContextType, ensureAudioGraph]);

    useEffect(() => {
        const gain = audioGainRef.current;
        if (!gain || !audioContextRef.current) return;
        if (currentMedia?.playbackContextType !== 'Music') {
            fadingOutRef.current = false;
            try { gain.gain.cancelScheduledValues(audioContextRef.current.currentTime); gain.gain.value = 1; } catch { /* ignore */ }
            return;
        }
        const crossfade = crossfadeStore.get();
        if (crossfade <= 0 || duration <= 0) return;
        const remaining = duration - currentTime;
        const nextItem = queue[queueIndex + 1];
        if (remaining > crossfade || remaining <= 0 || !nextItem) {
            if (!fadingOutRef.current) {
                try { gain.gain.cancelScheduledValues(audioContextRef.current.currentTime); gain.gain.value = 1; } catch { /* ignore */ }
            }
            return;
        }
        if (fadingOutRef.current) return;
        fadingOutRef.current = true;
        try {
            const now = audioContextRef.current.currentTime;
            gain.gain.cancelScheduledValues(now);
            gain.gain.setValueAtTime(gain.gain.value, now);
            gain.gain.linearRampToValueAtTime(0.01, now + remaining);
        } catch { /* ignore */ }
    }, [currentTime, duration, queue, queueIndex, currentMedia?.playbackContextType]);

    useEffect(() => {
        fadingOutRef.current = false;
        const gain = audioGainRef.current;
        const ctx = audioContextRef.current;
        if (!gain || !ctx) return;
        try {
            const crossfade = crossfadeStore.get();
            const now = ctx.currentTime;
            gain.gain.cancelScheduledValues(now);
            if (crossfade > 0 && currentMedia?.playbackContextType === 'Music') {
                gain.gain.setValueAtTime(0.01, now);
                gain.gain.linearRampToValueAtTime(1, now + Math.min(crossfade, 3));
            } else {
                gain.gain.value = 1;
            }
        } catch { /* ignore */ }
    }, [currentMedia?.id, currentMedia?.playbackContextType]);

    useEffect(() => {
        const onEqChange = () => applyEqPreset(eqPresetStore.get());
        window.addEventListener('audio-eq-changed', onEqChange);
        return () => window.removeEventListener('audio-eq-changed', onEqChange);
    }, [applyEqPreset]);

    useEffect(() => {
        const onQualityChange = () => {
            const media = currentMedia;
            if (!media || media.playbackContextType !== 'Music' || !media.id) return;
            const idx = queueIndexRef.current;
            const q = queueRef.current;
            const item = q[idx];
            if (!item) return;
            const server = media.serverId ? serverVault.getServer(media.serverId) : serverVault.getActiveServer();
            const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
            const newUrl = musicService.getTrackStreamUrl(media.id, baseUrl, audioQualityStore.get());
            const newItem = { ...item, streamUrl: newUrl };
            const updatedQueue = [...q];
            updatedQueue[idx] = newItem;
            setQueue(updatedQueue);
            queueRef.current = updatedQueue;
            const video = videoRef.current;
            const resumeAt = video?.currentTime ?? 0;
            setCurrentMedia(newItem);
            setTimeout(() => {
                const v = videoRef.current;
                if (v && resumeAt > 0) {
                    try { v.currentTime = resumeAt; } catch { /* ignore */ }
                }
            }, 200);
        };
        window.addEventListener('audio-quality-changed', onQualityChange);
        return () => window.removeEventListener('audio-quality-changed', onQualityChange);
    }, [currentMedia]);

    useEffect(() => {
        preloadedUrlRef.current = null;
        if (preloadAudioRef.current) {
            preloadAudioRef.current.removeAttribute('src');
            preloadAudioRef.current.load();
        }
    }, [currentMedia?.id]);

    useEffect(() => {
        if (currentMedia?.playbackContextType !== 'Music') return;
        if (duration <= 0) return;
        const remaining = duration - currentTime;
        if (remaining > 8 || remaining <= 0) return;
        const nextIndex = queueIndex + 1;
        const nextItem = queue[nextIndex];
        if (!nextItem || !nextItem.streamUrl) return;
        if (preloadedUrlRef.current === nextItem.streamUrl) return;
        const el = preloadAudioRef.current;
        if (!el) return;
        try {
            el.src = nextItem.streamUrl;
            el.load();
            preloadedUrlRef.current = nextItem.streamUrl;
        } catch {
            // best-effort
        }
    }, [currentTime, duration, queue, queueIndex, currentMedia?.playbackContextType]);

    useEffect(() => {
        const video = videoRef.current;
        if (!video) return;

        const updateTime = () => {
            setCurrentTime(video.currentTime);
            if (currentMedia?.playbackContextType === 'Music' && currentMedia.id) {
                if (recordedPlayTrackIdRef.current !== currentMedia.id) {
                    const t = video.currentTime;
                    const d = video.duration || 0;
                    if (t >= 30 || (d > 0 && t / d >= 0.5)) {
                        recordedPlayTrackIdRef.current = currentMedia.id;
                        musicService.recordPlay(currentMedia.id, Math.floor(t), false, currentMedia.serverId).catch(() => { /* ignore */ });
                    }
                }
            }
        };

        const onLoadedMetadata = () => {
            setDuration(video.duration);
            if (currentMedia?.startPosition && currentMedia.startPosition > 0) {
                video.currentTime = currentMedia.startPosition;
            }
        };

        const onDurationChange = () => setDuration(video.duration);
        const onPlay = () => setIsPlaying(true);
        const onPause = () => setIsPlaying(false);
        const onEnded = () => {
            setIsPlaying(false);
            if (currentMedia?.playbackContextType !== 'Music') return;
            if (currentMedia.id && recordedPlayTrackIdRef.current !== currentMedia.id) {
                recordedPlayTrackIdRef.current = currentMedia.id;
                musicService.recordPlay(currentMedia.id, Math.floor(video.currentTime), true, currentMedia.serverId).catch(() => { /* ignore */ });
            }
            const q = queueRef.current;
            const idx = queueIndexRef.current;
            const mode = repeatModeRef.current;

            if (mode === 'one' && q.length > 0) {
                playMedia(q[idx]);
                return;
            }
            if (idx < q.length - 1) {
                const newIndex = idx + 1;
                setQueueIndex(newIndex);
                queueIndexRef.current = newIndex;
                playMedia(q[newIndex]);
                return;
            }
            if (mode === 'all' && q.length > 0) {
                setQueueIndex(0);
                queueIndexRef.current = 0;
                playMedia(q[0]);
            }
        };

        video.addEventListener('timeupdate', updateTime);
        video.addEventListener('loadedmetadata', onLoadedMetadata);
        video.addEventListener('durationchange', onDurationChange);
        video.addEventListener('play', onPlay);
        video.addEventListener('pause', onPause);
        video.addEventListener('ended', onEnded);

        return () => {
            video.removeEventListener('timeupdate', updateTime);
            video.removeEventListener('loadedmetadata', onLoadedMetadata);
            video.removeEventListener('durationchange', onDurationChange);
            video.removeEventListener('play', onPlay);
            video.removeEventListener('pause', onPause);
            video.removeEventListener('ended', onEnded);
        };
    }, [currentMedia]);

    useEffect(() => {
        if (!sessionId) return;

        const ping = async () => {
            const currentPos = videoRef.current?.currentTime || 0;
            const currentDur = videoRef.current?.duration || 0;
            const isVideoPaused = videoRef.current?.paused ?? true;
            try {
                await streamingService.pingSession(sessionId, currentPos, currentDur, isVideoPaused, currentServerIdRef.current);
            } catch (error) {
                console.error("Failed to ping stream session", error);
            }
        };

        const interval = setInterval(ping, 10000);
        return () => clearInterval(interval);
    }, [sessionId]);

    const closePlayer = useCallback(async () => {
        if (sessionId) {
            try {
                const sessionServerId = currentMedia?.serverId;
                const finalPos = videoRef.current?.currentTime || 0;
                const finalDur = videoRef.current?.duration || 0;
                await streamingService.pingSession(sessionId, finalPos, finalDur, true, sessionServerId);
                await streamingService.stopSession(sessionId, sessionServerId);
            } catch (error) {
                console.error("Failed to stop session remotely", error);
            }
        }
        if (videoRef.current) {
            videoRef.current.pause();
            videoRef.current.src = "";
        }
        if (currentMedia?.playbackContextType === 'Music') {
            musicService.playbackStop(currentMedia.serverId).catch(() => { /* ignore */ });
        }
        setCurrentMedia(null);
        setSessionId(null);
        setIsPlaying(false);
        setQueue([]);
        setQueueIndex(0);
        queueRef.current = [];
        queueIndexRef.current = 0;
        linearQueueRef.current = [];
    }, [sessionId, currentMedia?.playbackContextType, currentMedia?.serverId]);

    useSignalREvent<{ sessionId: string, command: string, message?: string }>(
        "StreamCommandReceived",
        useCallback((payload) => {
            if (!sessionId || payload.sessionId !== sessionId) return;

            if (payload.command === 'play') videoRef.current?.play();
            else if (payload.command === 'pause') videoRef.current?.pause();
            else if (payload.command === 'stop') {
                closePlayer();
                setAdminMessage(payload.message || "The administrator has terminated your stream.");
            }
        }, [sessionId, closePlayer])
    );

    const playMedia = (media: PlayableMedia) => {
        setCurrentMedia(media);
        if (media.sessionId) setSessionId(media.sessionId);
        setIsMinimized(false);
        setIsPlaying(true);
    };

    const playQueue = (items: PlayableMedia[], startIndex = 0) => {
        if (items.length === 0) return;
        setRadioSeed(null);
        setRadioLabel(null);
        const safeIndex = Math.max(0, Math.min(startIndex, items.length - 1));
        linearQueueRef.current = [...items];

        let finalItems = items;
        let finalIndex = safeIndex;
        if (isShuffled) {
            const head = items[safeIndex];
            const rest = items.filter((_, i) => i !== safeIndex);
            for (let i = rest.length - 1; i > 0; i--) {
                const j = Math.floor(Math.random() * (i + 1));
                [rest[i], rest[j]] = [rest[j], rest[i]];
            }
            finalItems = [head, ...rest];
            finalIndex = 0;
        }

        setQueue(finalItems);
        setQueueIndex(finalIndex);
        queueRef.current = finalItems;
        queueIndexRef.current = finalIndex;
        playMedia(finalItems[finalIndex]);
    };

    const startRadio = (seed: RadioSeed, label: string, items: PlayableMedia[]) => {
        if (items.length === 0) return;
        linearQueueRef.current = [...items];
        setQueue(items);
        setQueueIndex(0);
        queueRef.current = items;
        queueIndexRef.current = 0;
        setRadioSeed(seed);
        setRadioLabel(label);
        playMedia(items[0]);
    };

    useEffect(() => {
        if (!radioSeed) return;
        if (queue.length === 0) return;
        if (radioExtendingRef.current) return;
        const remaining = queue.length - 1 - queueIndex;
        if (remaining > 3) return;

        radioExtendingRef.current = true;
        const excludeIds = queue.map(q => q.id);
        const sourceServerId = queue[queueIndex]?.serverId;
        const server = sourceServerId ? serverVault.getServer(sourceServerId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        const serverId = server?.id;

        musicService.extendRadio(radioSeed, excludeIds, 25, serverId)
            .then(result => {
                if (result.tracks.length === 0) return;
                const newItems: PlayableMedia[] = result.tracks.map(t => ({
                    id: t.id,
                    title: t.title,
                    subtitle: t.artist ?? '',
                    streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
                    serverId,
                    container: 'audio',
                    playbackContextType: 'Music'
                }));
                setQueue(prev => {
                    const merged = [...prev, ...newItems];
                    queueRef.current = merged;
                    linearQueueRef.current = merged;
                    return merged;
                });
            })
            .catch(err => {
                console.error('Radio extend failed', err);
            })
            .finally(() => {
                radioExtendingRef.current = false;
            });
    }, [queueIndex, queue, radioSeed]);

    const toggleShuffle = useCallback(() => {
        setIsShuffled(prev => {
            const next = !prev;
            const q = queueRef.current;
            const idx = queueIndexRef.current;
            if (q.length === 0) return next;

            const current = q[idx];
            if (next) {
                if (linearQueueRef.current.length === 0) {
                    linearQueueRef.current = [...q];
                }
                const rest = q.filter((_, i) => i !== idx);
                for (let i = rest.length - 1; i > 0; i--) {
                    const j = Math.floor(Math.random() * (i + 1));
                    [rest[i], rest[j]] = [rest[j], rest[i]];
                }
                const shuffled = [current, ...rest];
                setQueue(shuffled);
                setQueueIndex(0);
                queueRef.current = shuffled;
                queueIndexRef.current = 0;
            } else {
                const linear = linearQueueRef.current.length > 0 ? linearQueueRef.current : q;
                const newIdx = Math.max(0, linear.findIndex(i => i.id === current.id));
                setQueue(linear);
                setQueueIndex(newIdx);
                queueRef.current = linear;
                queueIndexRef.current = newIdx;
            }
            return next;
        });
    }, []);

    const cycleRepeatMode = useCallback(() => {
        setRepeatMode(prev => prev === 'off' ? 'all' : prev === 'all' ? 'one' : 'off');
    }, []);

    const addToQueue = useCallback((items: PlayableMedia[]) => {
        if (items.length === 0) return;
        const q = queueRef.current;
        if (q.length === 0) {
            // empty queue: start playback
            const newQueue = [...items];
            setQueue(newQueue);
            setQueueIndex(0);
            queueRef.current = newQueue;
            queueIndexRef.current = 0;
            linearQueueRef.current = [...newQueue];
            playMedia(newQueue[0]);
            return;
        }
        const newQueue = [...q, ...items];
        setQueue(newQueue);
        queueRef.current = newQueue;
        if (linearQueueRef.current.length > 0) {
            linearQueueRef.current = [...linearQueueRef.current, ...items];
        }
    }, []);

    const playNext = useCallback((items: PlayableMedia[]) => {
        if (items.length === 0) return;
        const q = queueRef.current;
        const idx = queueIndexRef.current;
        if (q.length === 0) {
            const newQueue = [...items];
            setQueue(newQueue);
            setQueueIndex(0);
            queueRef.current = newQueue;
            queueIndexRef.current = 0;
            linearQueueRef.current = [...newQueue];
            playMedia(newQueue[0]);
            return;
        }
        const newQueue = [...q.slice(0, idx + 1), ...items, ...q.slice(idx + 1)];
        setQueue(newQueue);
        queueRef.current = newQueue;
        if (linearQueueRef.current.length > 0) {
            const linearIdx = Math.max(0, linearQueueRef.current.findIndex(i => i.id === q[idx].id));
            linearQueueRef.current = [
                ...linearQueueRef.current.slice(0, linearIdx + 1),
                ...items,
                ...linearQueueRef.current.slice(linearIdx + 1)
            ];
        }
    }, []);

    const jumpToQueueIndex = useCallback((idx: number) => {
        const q = queueRef.current;
        if (idx < 0 || idx >= q.length) return;
        setQueueIndex(idx);
        queueIndexRef.current = idx;
        playMedia(q[idx]);
    }, []);

    const nextTrack = useCallback(() => {
        const q = queueRef.current;
        const idx = queueIndexRef.current;
        if (q.length === 0) return;
        if (idx < q.length - 1) {
            const newIndex = idx + 1;
            setQueueIndex(newIndex);
            queueIndexRef.current = newIndex;
            playMedia(q[newIndex]);
        } else if (repeatModeRef.current === 'all' && q.length > 0) {
            setQueueIndex(0);
            queueIndexRef.current = 0;
            playMedia(q[0]);
        } else {
            // end of queue, no repeat
            setQueue([]);
            setQueueIndex(0);
            queueRef.current = [];
            queueIndexRef.current = 0;
            if (videoRef.current) {
                videoRef.current.pause();
            }
            setIsPlaying(false);
        }
    }, []);

    const previousTrack = useCallback(() => {
        const q = queueRef.current;
        const idx = queueIndexRef.current;
        const video = videoRef.current;
        if (video && video.currentTime > 3) {
            video.currentTime = 0;
            setCurrentTime(0);
            return;
        }
        if (idx > 0) {
            const newIndex = idx - 1;
            setQueueIndex(newIndex);
            queueIndexRef.current = newIndex;
            playMedia(q[newIndex]);
        } else if (video) {
            video.currentTime = 0;
            setCurrentTime(0);
        }
    }, []);

    const hasNext = queue.length > 0 && queueIndex < queue.length - 1;
    const hasPrevious = queue.length > 0 && queueIndex > 0;

    const changeStreams = async (videoTrackId: string, audioTrackId: string, subtitleTrackId: string) => {
        if (!currentMedia) return;
        const currentPos = videoRef.current?.currentTime || 0;
        const sourceServerId = currentMedia.serverId;

        if (sessionId) {
            try {
                const currentDur = videoRef.current?.duration || 0;
                await streamingService.pingSession(sessionId, currentPos, currentDur, true, sourceServerId);
                await streamingService.stopSession(sessionId, sourceServerId);
            } catch (e) { console.error(e); }
        }

        try {
            const deviceId = localStorage.getItem('device_id') || 'unknown';
            const res = await streamingService.startSession(
                currentMedia.id,
                deviceId,
                currentPos,
                videoTrackId,
                audioTrackId,
                subtitleTrackId === 'none' ? undefined : subtitleTrackId,
                sourceServerId
            );

            const newMedia = {
                ...currentMedia,
                ...res,
                startPosition: currentPos
            };

            setSessionId(res.sessionId);
            setCurrentMedia(newMedia);
            setIsPlaying(true);
        } catch (e) {
            console.error("Failed to change streams", e);
            await dialog.alert("Failed to change streams.");
        }
    };

    const togglePlayPause = () => {
        if (!videoRef.current) return;
        if (isPlaying) videoRef.current.pause();
        else videoRef.current.play();
    };

    const seek = (time: number) => {
        if (videoRef.current) {
            videoRef.current.currentTime = time;
            setCurrentTime(time);
        }
    };

    const skipForward = (seconds: number = 30) => {
        if (videoRef.current) seek(videoRef.current.currentTime + seconds);
    };

    const skipBackward = (seconds: number = 10) => {
        if (videoRef.current) seek(Math.max(0, videoRef.current.currentTime - seconds));
    };

    const setVolume = (vol: number) => {
        if (videoRef.current) {
            videoRef.current.volume = vol;
            setVolumeState(vol);
        }
    };

    return (
        <PlayerContext.Provider value={{
            currentMedia, isPlaying, isMinimized, currentTime, duration, volume, sessionId,
            playMedia, playQueue, addToQueue, playNext, nextTrack, previousTrack, jumpToQueueIndex, hasNext, hasPrevious,
            queue, queueIndex, isShuffled, toggleShuffle, repeatMode, cycleRepeatMode,
            togglePlayPause, seek, skipForward, skipBackward,
            setMinimized: setIsMinimized,
            isFullscreen,
            toggleFullscreen: () => setIsFullscreen(v => !v),
            setFullscreen: setIsFullscreen,
            closePlayer, setVolume, changeStreams, videoRef,
            radioSeed, radioLabel, startRadio
        }}>
            {children}
            <audio ref={preloadAudioRef} preload="auto" style={{ display: 'none' }} muted />


            {adminMessage && (
                <div className="fixed inset-0 z-[9999] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4 transition-opacity">
                    <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl max-w-sm w-full p-6 text-center transform transition-all scale-100">
                        <div className="w-16 h-16 bg-red-900/30 text-red-500 rounded-full flex items-center justify-center mx-auto mb-4 border border-red-900/50 shadow-inner">
                            <svg className="w-8 h-8" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                            </svg>
                        </div>
                        <h2 className="text-2xl font-bold text-white mb-2 tracking-tight">Stream Terminated</h2>
                        <p className="text-gray-400 mb-8 text-sm leading-relaxed px-2">
                            {adminMessage}
                        </p>
                        <button
                            onClick={() => setAdminMessage(null)}
                            className="w-full py-3 bg-gray-800 hover:bg-gray-700 text-white font-bold rounded-lg transition-colors border border-gray-600 hover:border-gray-500 shadow-md cursor-pointer"
                        >
                            Dismiss
                        </button>
                    </div>
                </div>
            )}
        </PlayerContext.Provider>
    );
}

export const usePlayer = () => {
    const context = useContext(PlayerContext);
    if (!context) throw new Error("usePlayer must be used within a PlayerProvider");
    return context;
};