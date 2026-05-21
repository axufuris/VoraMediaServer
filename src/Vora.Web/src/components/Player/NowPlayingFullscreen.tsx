import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { usePlayer } from '../../contexts/usePlayer';
import { musicService, type LyricsVM } from '../../api/Music/musicService';
import { parseLrc, findActiveLineIndex, type LrcLine } from '../../utils/lrcParser';
import { audioQualityStore, crossfadeStore, eqPresetStore, type AudioQuality, type EqPreset } from '../../utils/audioQuality';
import { Modal } from '../Common/Modal';

const formatTime = (sec: number): string => {
    if (!isFinite(sec) || sec < 0) return '0:00';
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
};

export default function NowPlayingFullscreen() {
    const { serverId } = useParams<{ serverId?: string }>();
    const {
        currentMedia, isPlaying, currentTime, duration, isFullscreen, setFullscreen,
        togglePlayPause, nextTrack, previousTrack, hasNext, hasPrevious, seek,
        queue, queueIndex, jumpToQueueIndex,
        isShuffled, toggleShuffle, repeatMode, cycleRepeatMode, closePlayer,
        radioSeed, radioLabel,
    } = usePlayer();

    const [lyricsOpen, setLyricsOpen] = useState(false);
    const [queueOpen, setQueueOpen] = useState(false);
    const [audioSettingsOpen, setAudioSettingsOpen] = useState(false);
    const [audioQuality, setAudioQualityState] = useState<AudioQuality>(audioQualityStore.get());
    const [crossfadeSec, setCrossfadeSec] = useState<number>(crossfadeStore.get());
    const [eqPreset, setEqPresetState] = useState<EqPreset>(eqPresetStore.get());

    const updateAudioQuality = (v: AudioQuality) => {
        audioQualityStore.set(v);
        setAudioQualityState(v);
        window.dispatchEvent(new CustomEvent('audio-quality-changed'));
    };
    const updateCrossfade = (v: number) => { crossfadeStore.set(v); setCrossfadeSec(v); };
    const updateEqPreset = (v: EqPreset) => { eqPresetStore.set(v); setEqPresetState(v); window.dispatchEvent(new CustomEvent('audio-eq-changed')); };

    const [lyrics, setLyrics] = useState<LyricsVM | null>(null);
    const [lyricsLoading, setLyricsLoading] = useState(false);
    const [isLiked, setIsLiked] = useState(false);
    const [savingStation, setSavingStation] = useState(false);
    const [stationSaved, setStationSaved] = useState(false);
    const [stationName, setStationName] = useState<string>('');
    const [stationDialogOpen, setStationDialogOpen] = useState(false);
    const lyricsScrollRef = useRef<HTMLDivElement>(null);
    const lastActiveIndexRef = useRef(-1);

    useEffect(() => {
        setStationSaved(false);
        setStationName(radioLabel ?? '');
    }, [radioSeed, radioLabel]);

    const handleSaveStation = async () => {
        if (!radioSeed) return;
        const name = stationName.trim() || radioLabel || 'Radio Station';
        setSavingStation(true);
        try {
            await musicService.saveStation(name, radioSeed, serverId);
            setStationSaved(true);
            setStationDialogOpen(false);
            window.dispatchEvent(new CustomEvent('music-stations-changed'));
        } catch (err) {
            console.error('Failed to save station', err);
        } finally {
            setSavingStation(false);
        }
    };

    const parsedLrc = useMemo<LrcLine[]>(() => parseLrc(lyrics?.syncedLyrics), [lyrics?.syncedLyrics]);
    const activeLineIdx = lyrics?.isSynced ? findActiveLineIndex(parsedLrc, currentTime) : -1;

    useEffect(() => {
        if (!isFullscreen) return;
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') setFullscreen(false);
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [isFullscreen, setFullscreen]);

    useEffect(() => {
        if (!isFullscreen || !currentMedia) return;
        let cancelled = false;
        musicService.getLikedTracks(serverId).then(data => {
            if (cancelled) return;
            setIsLiked(data.tracks.some(t => t.id === currentMedia.id));
        }).catch(() => { /* ignore */ });
        return () => { cancelled = true; };
    }, [isFullscreen, currentMedia, serverId]);

    useEffect(() => {
        if (!isFullscreen || !currentMedia || !lyricsOpen) {
            return;
        }
        let cancelled = false;
        setLyricsLoading(true);
        setLyrics(null);
        musicService.getTrackLyrics(currentMedia.id, serverId)
            .then(data => { if (!cancelled) setLyrics(data); })
            .catch(() => { /* ignore */ })
            .finally(() => { if (!cancelled) setLyricsLoading(false); });
        return () => { cancelled = true; };
    }, [isFullscreen, currentMedia, serverId, lyricsOpen]);

    useEffect(() => {
        if (!lyricsOpen) return;
        if (activeLineIdx < 0 || activeLineIdx === lastActiveIndexRef.current) return;
        lastActiveIndexRef.current = activeLineIdx;
        const container = lyricsScrollRef.current;
        if (!container) return;
        const target = container.querySelector<HTMLDivElement>(`[data-line="${activeLineIdx}"]`);
        if (!target) return;
        const containerRect = container.getBoundingClientRect();
        const targetRect = target.getBoundingClientRect();
        const offset = (targetRect.top - containerRect.top) - (container.clientHeight / 2) + (target.clientHeight / 2);
        container.scrollTo({ top: container.scrollTop + offset, behavior: 'smooth' });
    }, [activeLineIdx, lyricsOpen]);

    const toggleLike = async () => {
        if (!currentMedia) return;
        const next = !isLiked;
        setIsLiked(next);
        try {
            if (next) await musicService.likeTrack(currentMedia.id, serverId);
            else await musicService.unlikeTrack(currentMedia.id, serverId);
            window.dispatchEvent(new CustomEvent('music-likes-changed'));
        } catch (err) {
            console.error('Toggle like failed', err);
            setIsLiked(!next);
        }
    };

    const onScrubChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const v = parseFloat(e.target.value);
        if (!isNaN(v)) seek(v);
    };

    if (!isFullscreen || !currentMedia || currentMedia.playbackContextType !== 'Music') return null;

    const posterUrl = currentMedia.posterUrl;
    const repeatIcon = repeatMode === 'one' ? (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
            <path d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z" />
            <text x="12" y="14" textAnchor="middle" fontSize="7" fill="currentColor" fontWeight="bold" style={{ fill: 'var(--vora-bg-canvas)' }}>1</text>
        </svg>
    ) : (
        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
            <path d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z" />
        </svg>
    );

    const headerActionStyle = (active: boolean) => ({
        background: active ? 'var(--vora-accent-soft)' : 'rgba(255, 255, 255, 0.06)',
        color: active ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
        border: `1px solid ${active ? 'var(--vora-accent-soft-hover)' : 'var(--vora-border-subtle)'}`,
    });

    return (
        <div className="fixed inset-0 z-[100000] flex flex-col overflow-hidden" style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-primary)' }}>
            <div className="absolute inset-0 z-0">
                {posterUrl ? (
                    <img
                        key={currentMedia.id}
                        src={posterUrl}
                        alt=""
                        className="h-full w-full object-cover transition-opacity duration-700"
                        style={{ filter: 'blur(60px) saturate(140%)', opacity: 0.5, transform: 'scale(1.15)' }}
                    />
                ) : (
                    <div className="h-full w-full" style={{ background: 'radial-gradient(circle at 30% 20%, color-mix(in srgb, var(--vora-accent-500) 25%, transparent), transparent 60%), var(--vora-bg-canvas)' }} />
                )}
                <div className="absolute inset-0" style={{ background: 'linear-gradient(180deg, color-mix(in srgb, var(--vora-bg-canvas) 30%, transparent) 0%, color-mix(in srgb, var(--vora-bg-canvas) 60%, transparent) 50%, var(--vora-bg-canvas) 100%)' }} />
            </div>

            <header
                className="relative z-20 grid shrink-0 grid-cols-3 items-center px-6 py-4 vora-glass"
                style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}
            >
                <div className="flex justify-start">
                    <button
                        type="button"
                        onClick={() => setFullscreen(false)}
                        aria-label="Minimize"
                        title="Minimize (Esc)"
                        className="inline-flex h-9 w-9 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-secondary)' }}
                    >
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="6 9 12 15 18 9" /></svg>
                    </button>
                </div>
                <div className="truncate text-center text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>
                    {radioSeed ? (radioLabel ?? 'Radio') : 'Now Playing'}
                </div>
                <div className="flex items-center justify-end gap-1.5">
                    {radioSeed && (
                        <button
                            type="button"
                            onClick={() => { if (stationSaved) return; setStationDialogOpen(true); }}
                            disabled={stationSaved}
                            title={stationSaved ? 'Station saved' : 'Save this radio as a station you can replay later'}
                            className="inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors disabled:cursor-default disabled:opacity-80"
                            style={headerActionStyle(stationSaved)}
                        >
                            <svg width="14" height="14" viewBox="0 0 24 24" fill={stationSaved ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2"><path d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" /></svg>
                            {stationSaved ? 'Saved' : 'Save station'}
                        </button>
                    )}
                    <button
                        type="button"
                        onClick={() => setAudioSettingsOpen(v => !v)}
                        title="Audio settings"
                        className="inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                        style={headerActionStyle(audioSettingsOpen)}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3" /></svg>
                        Audio
                    </button>
                    <button
                        type="button"
                        onClick={() => setLyricsOpen(v => !v)}
                        title="Toggle lyrics"
                        className="inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                        style={headerActionStyle(lyricsOpen)}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 6h16M4 12h10M4 18h7" /></svg>
                        Lyrics
                    </button>
                    <button
                        type="button"
                        onClick={() => setQueueOpen(v => !v)}
                        title="Toggle queue"
                        className="inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors"
                        style={headerActionStyle(queueOpen)}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="4" y1="6" x2="20" y2="6" /><line x1="4" y1="12" x2="20" y2="12" /><line x1="4" y1="18" x2="14" y2="18" /><polygon points="17 16 22 19 17 22 17 16" fill="currentColor" /></svg>
                        Queue
                    </button>
                    <button
                        type="button"
                        onClick={() => { setFullscreen(false); closePlayer(); }}
                        aria-label="Stop and close player"
                        title="Stop & close player"
                        className="ml-1 inline-flex h-9 w-9 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-secondary)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>
            </header>

            <div className="relative z-10 flex min-h-0 flex-1">
                <div className="flex min-h-0 min-w-0 flex-1 flex-col items-center px-8 pb-4">
                    <div
                        className={`shrink-0 transition-all duration-500 ${lyricsOpen ? 'mt-2' : 'flex flex-1 items-end pb-6'}`}
                    >
                        <div
                            key={currentMedia.id}
                            className={`aspect-square overflow-hidden transition-all duration-500 ${lyricsOpen ? 'w-[200px]' : 'w-[min(420px,55vh)]'}`}
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-overlay)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-sunken)',
                            }}
                        >
                            {posterUrl ? (
                                <img src={posterUrl} alt={currentMedia.title} className="h-full w-full object-cover" />
                            ) : (
                                <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-disabled)' }}>
                                    <svg width="96" height="96" viewBox="0 0 24 24" fill="currentColor"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="mt-5 w-full max-w-[640px] shrink-0 text-center">
                        <h1
                            className={`m-0 truncate font-semibold transition-all duration-300 ${lyricsOpen ? 'text-2xl' : 'text-3xl'}`}
                            style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}
                            title={currentMedia.title}
                        >
                            {currentMedia.title}
                        </h1>
                        {currentMedia.subtitle && (
                            <p
                                className="mt-1.5 truncate text-base"
                                style={{ color: 'var(--vora-text-secondary)' }}
                                title={currentMedia.subtitle}
                            >
                                {currentMedia.subtitle}
                            </p>
                        )}
                    </div>

                    {lyricsOpen && (
                        <div ref={lyricsScrollRef} className="mt-6 min-h-0 w-full max-w-[640px] flex-1 overflow-y-auto px-4">
                            {lyricsLoading ? (
                                <div className="py-16 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>Loading lyrics…</div>
                            ) : !lyrics ? (
                                <div className="py-16 text-center text-sm" style={{ color: 'var(--vora-text-disabled)' }}>No lyrics found for this track.</div>
                            ) : lyrics.isSynced && parsedLrc.length > 0 ? (
                                <div className="space-y-3 py-[35vh]">
                                    {parsedLrc.map((line, i) => {
                                        const isActive = i === activeLineIdx;
                                        return (
                                            <div
                                                key={i}
                                                data-line={i}
                                                onClick={() => seek(line.time)}
                                                className={`cursor-pointer rounded px-2 py-1 text-center transition-all ${isActive ? 'text-2xl font-semibold' : 'text-base'}`}
                                                style={{ color: isActive ? 'var(--vora-text-primary)' : 'var(--vora-text-muted)' }}
                                            >
                                                {line.text || '♪'}
                                            </div>
                                        );
                                    })}
                                </div>
                            ) : (
                                <pre className="whitespace-pre-wrap py-6 text-center font-sans text-base leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>{lyrics.plainLyrics || ''}</pre>
                            )}
                            {lyrics?.providerName && (
                                <div className="pb-4 text-center text-[10px]" style={{ color: 'var(--vora-text-disabled)' }}>Lyrics via {lyrics.providerName}</div>
                            )}
                        </div>
                    )}
                </div>

                {queueOpen && (
                    <aside
                        className="flex w-[340px] shrink-0 flex-col"
                        style={{ background: 'color-mix(in srgb, var(--vora-bg-surface) 80%, transparent)', borderLeft: '1px solid var(--vora-border-subtle)' }}
                    >
                        <div className="shrink-0 px-5 py-4 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)', borderBottom: '1px solid var(--vora-border-subtle)' }}>
                            Queue · {queue.length}
                        </div>
                        <div className="min-h-0 flex-1 space-y-1 overflow-y-auto p-2">
                            {queue.length === 0 ? (
                                <div className="py-12 text-center text-xs" style={{ color: 'var(--vora-text-disabled)' }}>Queue is empty.</div>
                            ) : queue.map((item, idx) => {
                                const isCurrent = idx === queueIndex;
                                return (
                                    <button
                                        key={`${item.id}-${idx}`}
                                        type="button"
                                        onClick={() => jumpToQueueIndex(idx)}
                                        className="flex w-full cursor-pointer items-center gap-3 rounded-md p-2 text-left transition-colors"
                                        style={{
                                            background: isCurrent ? 'var(--vora-accent-soft)' : 'transparent',
                                            border: `1px solid ${isCurrent ? 'var(--vora-accent-soft-hover)' : 'transparent'}`,
                                        }}
                                    >
                                        <div
                                            className="h-10 w-10 shrink-0 overflow-hidden rounded"
                                            style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                                        >
                                            {item.posterUrl ? <img src={item.posterUrl} alt="" className="h-full w-full object-cover" /> : null}
                                        </div>
                                        <div className="min-w-0 flex-1">
                                            <div className="truncate text-sm font-medium" style={{ color: isCurrent ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}>{item.title}</div>
                                            {item.subtitle && <div className="truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{item.subtitle}</div>}
                                        </div>
                                    </button>
                                );
                            })}
                        </div>
                    </aside>
                )}
            </div>

            <div className="relative z-10 shrink-0 px-8 pb-7 pt-2">
                <div className="mb-2 flex items-center gap-3 text-xs tabular-nums" style={{ color: 'var(--vora-text-muted)' }}>
                    <span className="w-10 text-right">{formatTime(currentTime)}</span>
                    <input
                        type="range"
                        min={0}
                        max={duration || 0}
                        step={0.1}
                        value={currentTime}
                        onChange={onScrubChange}
                        className="flex-1 cursor-pointer accent-[var(--vora-accent-500)]"
                    />
                    <span className="w-10">{formatTime(duration)}</span>
                </div>
                <div className="flex items-center justify-center gap-6">
                    <button
                        type="button"
                        onClick={toggleShuffle}
                        title={isShuffled ? 'Shuffle: on' : 'Shuffle: off'}
                        className="inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: isShuffled ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                    >
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                    </button>
                    <button
                        type="button"
                        onClick={previousTrack}
                        disabled={!hasPrevious}
                        title="Previous"
                        className="inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5 disabled:cursor-default disabled:opacity-30"
                        style={{ color: 'var(--vora-text-primary)' }}
                    >
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                    </button>
                    <button
                        type="button"
                        onClick={togglePlayPause}
                        title={isPlaying ? 'Pause' : 'Play'}
                        className="flex h-16 w-16 cursor-pointer items-center justify-center rounded-full transition-transform hover:scale-105"
                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', boxShadow: 'var(--vora-shadow-lg)' }}
                    >
                        {isPlaying ? (
                            <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor"><path d="M6 4h4v16H6zM14 4h4v16h-4z" /></svg>
                        ) : (
                            <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" style={{ marginLeft: 3 }}><path d="M8 5v14l11-7z" /></svg>
                        )}
                    </button>
                    <button
                        type="button"
                        onClick={nextTrack}
                        disabled={!hasNext}
                        title="Next"
                        className="inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5 disabled:cursor-default disabled:opacity-30"
                        style={{ color: 'var(--vora-text-primary)' }}
                    >
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor"><path d="M6 18l8.5-6L6 6v12zM16 6h2v12h-2z" /></svg>
                    </button>
                    <button
                        type="button"
                        onClick={cycleRepeatMode}
                        title={`Repeat: ${repeatMode}`}
                        className="inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: repeatMode !== 'off' ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                    >
                        {repeatIcon}
                    </button>
                    <button
                        type="button"
                        onClick={toggleLike}
                        title={isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                        className="ml-2 inline-flex h-10 w-10 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: isLiked ? 'var(--vora-accent-text)' : 'var(--vora-text-muted)' }}
                    >
                        {isLiked ? (
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                        ) : (
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path strokeLinecap="round" strokeLinejoin="round" d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" /></svg>
                        )}
                    </button>
                </div>
            </div>

            {audioSettingsOpen && (
                <div
                    className="fixed right-6 top-20 z-[105000] w-80 rounded-xl p-5"
                    style={{
                        background: 'var(--vora-bg-raised)',
                        border: '1px solid var(--vora-border-strong)',
                        boxShadow: 'var(--vora-shadow-overlay)',
                    }}
                >
                    <div className="mb-4 flex items-center justify-between">
                        <h3 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Audio settings</h3>
                        <button
                            type="button"
                            onClick={() => setAudioSettingsOpen(false)}
                            aria-label="Close"
                            className="inline-flex h-7 w-7 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                        </button>
                    </div>

                    <div className="space-y-4">
                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Audio quality</label>
                            <select
                                value={audioQuality}
                                onChange={e => updateAudioQuality(e.target.value as AudioQuality)}
                                className="w-full cursor-pointer rounded-md p-2 text-sm outline-none transition-colors"
                                style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}
                            >
                                <option value="Auto">Auto (Original)</option>
                                <option value="High">High (320 kbps)</option>
                                <option value="Medium">Medium (192 kbps)</option>
                                <option value="Low">Low (128 kbps)</option>
                                <option value="Original">Original (no transcoding)</option>
                            </select>
                            <p className="mt-1.5 text-[10px]" style={{ color: 'var(--vora-text-muted)' }}>Lower for mobile / slow connections. Changes apply to the next track.</p>
                        </div>

                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Crossfade: {crossfadeSec === 0 ? 'Off' : `${crossfadeSec}s`}</label>
                            <input
                                type="range"
                                min={0}
                                max={12}
                                step={1}
                                value={crossfadeSec}
                                onChange={e => updateCrossfade(parseInt(e.target.value, 10))}
                                className="w-full cursor-pointer accent-[var(--vora-accent-500)]"
                            />
                            <p className="mt-1.5 text-[10px]" style={{ color: 'var(--vora-text-muted)' }}>Smooth volume fade as each track approaches its end.</p>
                        </div>

                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>EQ preset</label>
                            <select
                                value={eqPreset}
                                onChange={e => updateEqPreset(e.target.value as EqPreset)}
                                className="w-full cursor-pointer rounded-md p-2 text-sm outline-none transition-colors"
                                style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}
                            >
                                <option value="Off">Off (Flat)</option>
                                <option value="BassBoost">Bass Boost</option>
                                <option value="TrebleBoost">Treble Boost</option>
                                <option value="Vocal">Vocal Clarity</option>
                                <option value="Loudness">Loudness</option>
                            </select>
                            <p className="mt-1.5 text-[10px]" style={{ color: 'var(--vora-text-muted)' }}>EQ applies in real time. Bass Boost adds low end, Vocal boosts the mids, etc.</p>
                        </div>
                    </div>
                </div>
            )}

            <Modal
                isOpen={stationDialogOpen}
                onClose={() => { if (!savingStation) setStationDialogOpen(false); }}
                size="md"
                surface="gray-900"
                zIndex="z-[100]"
                closeOnBackdropClick={!savingStation}
            >
                <div className="p-6">
                    <h3 className="m-0 mb-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Save station</h3>
                    <p className="m-0 mb-4 text-sm leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                        Save the current radio so you can replay it from your library later. It will generate a fresh queue each time.
                    </p>
                    <label className="mb-2 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Name</label>
                    <input
                        autoFocus
                        type="text"
                        value={stationName}
                        onChange={e => setStationName(e.target.value)}
                        placeholder={radioLabel ?? 'Radio station'}
                        className="mb-5 w-full rounded-md p-3 text-sm outline-none transition-colors"
                        style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}
                    />
                    <div className="flex justify-end gap-2">
                        <button
                            type="button"
                            onClick={() => setStationDialogOpen(false)}
                            disabled={savingStation}
                            className="vora-button-secondary cursor-pointer disabled:opacity-50"
                        >
                            Cancel
                        </button>
                        <button
                            type="button"
                            onClick={handleSaveStation}
                            disabled={savingStation}
                            className="vora-button-primary cursor-pointer disabled:opacity-50"
                        >
                            {savingStation ? 'Saving…' : 'Save'}
                        </button>
                    </div>
                </div>
            </Modal>
        </div>
    );
}
