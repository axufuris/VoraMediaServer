import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { usePlayer, usePlayerTime } from '../../contexts/usePlayer';
import { musicService, type LyricsVM } from '../../api/Music/musicService';
import { parseLrc, findActiveLineIndex, type LrcLine } from '../../utils/lrcParser';
import { audioQualityStore, crossfadeStore, eqPresetStore, type AudioQuality, type EqPreset } from '../../utils/audioQuality';
import { Modal } from '../Common/Modal';
import { NowPlayingShell } from './NowPlaying/NowPlayingShell';
import { NowPlayingArtwork } from './NowPlaying/NowPlayingArtwork';
import { NowPlayingControlRow, NowPlayingIconButton, NowPlayingPill, NowPlayingPlayButton, NowPlayingSeekBar, NowPlayingVolume } from './NowPlaying/NowPlayingControls';
import { lyricsScrollTop } from '../../utils/lyricsScroll';
import AddToPlaylistModal from '../Collections/AddToPlaylistModal';

export default function NowPlayingFullscreen() {
    const { serverId } = useParams<{ serverId?: string }>();
    const {
        currentMedia, isPlaying, isFullscreen, setFullscreen,
        togglePlayPause, nextTrack, previousTrack, hasNext, hasPrevious, seek,
        queue, queueIndex, jumpToQueueIndex,
        isShuffled, toggleShuffle, repeatMode, cycleRepeatMode, closePlayer,
        radioSeed, radioLabel, volume, setVolume,
    } = usePlayer();
    const { currentTime, duration } = usePlayerTime();

    const playButtonRef = useRef<HTMLButtonElement>(null);
    const minimize = useCallback(() => setFullscreen(false), [setFullscreen]);

    const [lyricsWanted, setLyricsWanted] = useState(false);
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
    const [addToPlaylistOpen, setAddToPlaylistOpen] = useState(false);
    const lyricsScrollRef = useRef<HTMLDivElement>(null);
    const lastLyricsScrollRef = useRef<string | null>(null);

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

    const hasLyrics = parsedLrc.length > 0 || !!lyrics?.plainLyrics?.trim();

    // Turning lyrics on is a preference that carries from track to track. While
    // the next track's lyrics load the panel stays open on its loading state
    // rather than collapsing and springing back; a track with none closes it
    // without clearing the preference for the one after.
    const lyricsOpen = lyricsWanted && (hasLyrics || lyricsLoading);
    const showLyricsToggle = hasLyrics || lyricsOpen;
    // Plain lyrics carry no timings, so they are shown as what they are: a block
    // of text the listener scrolls themselves. The panel used to scroll them in
    // proportion to the song's progress and lock manual scrolling, which read as
    // broken sync — a proportional position lands mid-verse whenever the text
    // has repeated choruses or section headers, and the listener could not
    // scroll back to where the singer actually was.
    const plainLyricsOpen = lyricsOpen && !!lyrics && !(lyrics.isSynced && parsedLrc.length > 0);

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
        if (!isFullscreen || !currentMedia) {
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
    }, [isFullscreen, currentMedia, serverId]);

    const currentTrackId = currentMedia?.id;

    // A new track starts at the top of its own text rather than wherever the
    // listener had scrolled the previous one to.
    useEffect(() => {
        if (!plainLyricsOpen) return;
        const container = lyricsScrollRef.current;
        if (container) container.scrollTop = 0;
    }, [plainLyricsOpen, currentTrackId]);

    useEffect(() => {
        if (!lyricsOpen) {
            lastLyricsScrollRef.current = null;
            return;
        }
        const container = lyricsScrollRef.current;
        if (!container) return;
        const index = Math.max(activeLineIdx, 0);
        const target = container.querySelector<HTMLDivElement>(`[data-line="${index}"]`);
        if (!target) return;

        const key = `${currentTrackId}:${index}`;
        const previous = lastLyricsScrollRef.current;
        if (key === previous) return;
        lastLyricsScrollRef.current = key;

        const lineTop = target.getBoundingClientRect().top - container.getBoundingClientRect().top + container.scrollTop;
        const top = lyricsScrollTop(lineTop, target.clientHeight, container.clientHeight);
        const followingAlong = previous?.startsWith(`${currentTrackId}:`) ?? false;
        container.scrollTo({ top, behavior: followingAlong ? 'smooth' : 'auto' });
    }, [activeLineIdx, lyricsOpen, parsedLrc, currentTrackId]);

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

    const posterFallback = <svg width="96" height="96" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>;

    return (
        <NowPlayingShell
            artworkKey={currentMedia.id}
            posterUrl={posterUrl}
            label={radioSeed ? (radioLabel ?? 'Radio') : 'Now Playing'}
            onMinimize={minimize}
            onClose={() => { setFullscreen(false); closePlayer(); }}
            initialFocusRef={playButtonRef}
            headerActions={radioSeed && (
                <NowPlayingPill
                    label={stationSaved ? 'Saved' : 'Save station'}
                    title={stationSaved ? 'Station saved' : 'Save this radio as a station you can replay later'}
                    active={stationSaved}
                    disabled={stationSaved}
                    onClick={() => { if (!stationSaved) setStationDialogOpen(true); }}
                    icon={<svg width="16" height="16" viewBox="0 0 24 24" fill={stationSaved ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" /></svg>}
                />
            )}
            controls={
                <>
                    <NowPlayingSeekBar currentTime={currentTime} duration={duration} onSeek={seek} />
                    <NowPlayingControlRow
                        transport={
                            <>
                                <NowPlayingIconButton label={isShuffled ? 'Shuffle: on' : 'Shuffle: off'} pressed={isShuffled} active={isShuffled} onClick={toggleShuffle}>
                                    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M10.59 9.17 5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z" /></svg>
                                </NowPlayingIconButton>
                                <NowPlayingIconButton label="Previous" emphasis="primary" disabled={!hasPrevious} onClick={previousTrack}>
                                    <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                                </NowPlayingIconButton>
                                <NowPlayingPlayButton isPlaying={isPlaying} onClick={togglePlayPause} buttonRef={playButtonRef} />
                                <NowPlayingIconButton label="Next" emphasis="primary" disabled={!hasNext} onClick={nextTrack}>
                                    <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 18l8.5-6L6 6v12zM16 6h2v12h-2z" /></svg>
                                </NowPlayingIconButton>
                                <NowPlayingIconButton label={`Repeat: ${repeatMode}`} pressed={repeatMode !== 'off'} active={repeatMode !== 'off'} onClick={cycleRepeatMode}>
                                    {repeatIcon}
                                </NowPlayingIconButton>
                            </>
                        }
                        actions={
                            <>
                                <NowPlayingIconButton
                                    label={isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                                    pressed={isLiked}
                                    active={isLiked}
                                    onClick={toggleLike}
                                >
                                    {isLiked ? (
                                        <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                                    ) : (
                                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" /></svg>
                                    )}
                                </NowPlayingIconButton>
                                {!radioSeed && (
                                    <NowPlayingPill
                                        label="Playlist"
                                        title="Add this track to a playlist"
                                        active={addToPlaylistOpen}
                                        onClick={() => setAddToPlaylistOpen(true)}
                                        icon={<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><line x1="3" y1="6" x2="15" y2="6" /><line x1="3" y1="12" x2="15" y2="12" /><line x1="3" y1="18" x2="11" y2="18" /><line x1="18" y1="9" x2="18" y2="19" /><line x1="13" y1="14" x2="23" y2="14" /></svg>}
                                    />
                                )}
                                {showLyricsToggle && (
                                    <NowPlayingPill
                                        label="Lyrics"
                                        title={lyricsOpen ? 'Hide lyrics' : 'Show lyrics'}
                                        active={lyricsOpen}
                                        onClick={() => setLyricsWanted(!lyricsOpen)}
                                        icon={<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 5h12M3 10h12M3 15h7" /><path d="M19 17V5l3 1.5" /><circle cx="17" cy="17" r="2" /></svg>}
                                    />
                                )}
                                <NowPlayingPill
                                    label="Queue"
                                    title={queueOpen ? 'Hide queue' : 'Show queue'}
                                    active={queueOpen}
                                    onClick={() => setQueueOpen(v => !v)}
                                    icon={<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><line x1="4" y1="6" x2="20" y2="6" /><line x1="4" y1="12" x2="20" y2="12" /><line x1="4" y1="18" x2="14" y2="18" /><polygon points="17 16 22 19 17 22 17 16" fill="currentColor" /></svg>}
                                />
                                <NowPlayingPill
                                    label="Audio"
                                    title="Audio settings"
                                    active={audioSettingsOpen}
                                    controls="now-playing-audio-settings"
                                    onClick={() => setAudioSettingsOpen(v => !v)}
                                    icon={<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><line x1="4" y1="21" x2="4" y2="14" /><line x1="4" y1="10" x2="4" y2="3" /><line x1="12" y1="21" x2="12" y2="12" /><line x1="12" y1="8" x2="12" y2="3" /><line x1="20" y1="21" x2="20" y2="16" /><line x1="20" y1="12" x2="20" y2="3" /><line x1="1" y1="14" x2="7" y2="14" /><line x1="9" y1="8" x2="15" y2="8" /><line x1="17" y1="16" x2="23" y2="16" /></svg>}
                                />
                                <NowPlayingVolume value={volume} onChange={setVolume} />
                            </>
                        }
                    />
                    {audioSettingsOpen && (
                        <div
                            id="now-playing-audio-settings"
                            className="absolute bottom-full right-4 z-20 mb-2 w-80 max-w-[calc(100vw-2rem)] rounded-xl p-5 md:right-8"
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
                </>
            }
            overlays={
                <>
                    {addToPlaylistOpen && currentMedia && (
                        <AddToPlaylistModal
                            isOpen={true}
                            onClose={() => setAddToPlaylistOpen(false)}
                            mediaId={currentMedia.id}
                            kind="music"
                            serverId={currentMedia.serverId ?? serverId}
                        />
                    )}
                    <Modal
                        isOpen={stationDialogOpen}
                        onClose={() => { if (!savingStation) setStationDialogOpen(false); }}
                        size="md"
                        surface="gray-900"
                        zIndex="z-[210]"
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
                </>
            }
        >
            <div className="flex min-h-0 min-w-0 flex-1 flex-col items-center px-8 pb-4">
                <NowPlayingArtwork
                    artworkKey={currentMedia.id}
                    posterUrl={posterUrl}
                    title={currentMedia.title}
                    subtitle={currentMedia.subtitle}
                    compact={lyricsOpen}
                    fallbackIcon={posterFallback}
                />

                {lyricsOpen && (
                    <div
                        ref={lyricsScrollRef}
                        data-testid="lyrics-panel"
                        aria-label="Lyrics"
                        tabIndex={plainLyricsOpen ? 0 : undefined}
                        className="mt-6 min-h-0 w-full max-w-[640px] flex-1 overflow-y-auto px-4"
                    >
                        {lyricsLoading ? (
                            <div className="py-16 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>Loading lyrics…</div>
                        ) : !lyrics ? (
                            <div className="py-16 text-center text-sm" style={{ color: 'var(--vora-text-disabled)' }}>No lyrics found for this track.</div>
                        ) : lyrics.isSynced && parsedLrc.length > 0 ? (
                            <div className="space-y-3 pb-[60vh] pt-6">
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
                            <>
                                {/* Said once, quietly, so the absence of a moving
                                    highlight reads as a kind of lyrics rather than
                                    as sync that has stopped working. */}
                                <div className="pt-4 text-center text-[11px] uppercase tracking-widest" style={{ color: 'var(--vora-text-disabled)' }}>
                                    Not synced to this recording
                                </div>
                                <pre className="whitespace-pre-wrap py-4 text-center font-sans text-base leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>{lyrics.plainLyrics || ''}</pre>
                            </>
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
        </NowPlayingShell>
    );
}
