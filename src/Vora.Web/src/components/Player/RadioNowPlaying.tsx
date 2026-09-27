import { useRef, useState } from 'react';
import type { PlayableMedia } from '../../contexts/usePlayer';
import type { IptvChannelVM } from '../../api/Iptv/iptvAdminService';
import { NowPlayingShell } from './NowPlaying/NowPlayingShell';
import { NowPlayingArtwork } from './NowPlaying/NowPlayingArtwork';
import { NowPlayingControlRow, NowPlayingIconButton, NowPlayingPill, NowPlayingPlayButton, NowPlayingVolume } from './NowPlaying/NowPlayingControls';

// Live radio's full screen, built from the same pieces as the music screen.
// There is no seek bar and no skip back/forward: a live stream has no position
// to move to. Previous/next change station instead.
interface RadioNowPlayingProps {
    station: PlayableMedia;
    stations: IptvChannelVM[];
    isPlaying: boolean;
    isLoading: boolean;
    streamError: string | null;
    volume: number;
    onVolumeChange: (value: number) => void;
    onTogglePlay: () => void;
    onPreviousStation: () => void;
    onNextStation: () => void;
    onSelectStation: (station: IptvChannelVM) => void;
    onMinimize: () => void;
    onClose: () => void;
}

const radioIcon = <svg width="96" height="96" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 1a9 9 0 00-9 9v7c0 1.66 1.34 3 3 3h3v-8H5v-2a7 7 0 1114 0v2h-4v8h3c1.66 0 3-1.34 3-3v-7a9 9 0 00-9-9z" /></svg>;

export default function RadioNowPlaying({
    station,
    stations,
    isPlaying,
    isLoading,
    streamError,
    volume,
    onVolumeChange,
    onTogglePlay,
    onPreviousStation,
    onNextStation,
    onSelectStation,
    onMinimize,
    onClose,
}: RadioNowPlayingProps) {
    const playButtonRef = useRef<HTMLButtonElement>(null);
    const [stationsOpen, setStationsOpen] = useState(false);
    const canChangeStation = stations.length > 1;

    const status = streamError ? (
        <p className="mx-auto mt-3 max-w-md text-sm" style={{ color: 'var(--vora-text-secondary)' }} role="alert">{streamError}</p>
    ) : isLoading ? (
        <p className="mt-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }} aria-live="polite">Tuning in…</p>
    ) : (
        <div className="mt-3 flex items-center justify-center gap-2" aria-live="polite">
            <span className="h-2 w-2 animate-pulse rounded-full" style={{ background: 'var(--vora-accent-500)' }} />
            <span className="text-xs font-bold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>On air</span>
        </div>
    );

    return (
        <NowPlayingShell
            artworkKey={station.id}
            posterUrl={station.posterUrl}
            label="Live Radio"
            onMinimize={onMinimize}
            onClose={onClose}
            initialFocusRef={playButtonRef}
            controls={
                <NowPlayingControlRow
                    transport={
                        <>
                            <NowPlayingIconButton label="Previous station" emphasis="primary" disabled={!canChangeStation} onClick={onPreviousStation}>
                                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 6h2v12H6zm3.5 6l8.5 6V6z" /></svg>
                            </NowPlayingIconButton>
                            <NowPlayingPlayButton isPlaying={isPlaying} onClick={onTogglePlay} buttonRef={playButtonRef} />
                            <NowPlayingIconButton label="Next station" emphasis="primary" disabled={!canChangeStation} onClick={onNextStation}>
                                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 18l8.5-6L6 6v12zM16 6h2v12h-2z" /></svg>
                            </NowPlayingIconButton>
                        </>
                    }
                    actions={
                        <>
                            {stations.length > 0 && (
                                <NowPlayingPill
                                    label="Stations"
                                    title={stationsOpen ? 'Hide stations' : 'Show stations'}
                                    active={stationsOpen}
                                    onClick={() => setStationsOpen(v => !v)}
                                    icon={<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true"><line x1="8" y1="6" x2="21" y2="6" /><line x1="8" y1="12" x2="21" y2="12" /><line x1="8" y1="18" x2="21" y2="18" /><circle cx="4" cy="6" r="1" /><circle cx="4" cy="12" r="1" /><circle cx="4" cy="18" r="1" /></svg>}
                                />
                            )}
                            <NowPlayingVolume value={volume} onChange={onVolumeChange} />
                        </>
                    }
                />
            }
        >
            <div className="flex min-h-0 min-w-0 flex-1 flex-col items-center px-8 pb-4">
                <NowPlayingArtwork
                    artworkKey={station.id}
                    posterUrl={station.posterUrl}
                    title={station.title}
                    subtitle={station.subtitle || 'Live Radio'}
                    meta={status}
                    fallbackIcon={radioIcon}
                    fit="contain"
                />
            </div>

            {stationsOpen && (
                <aside
                    className="flex w-[340px] shrink-0 flex-col"
                    style={{ background: 'color-mix(in srgb, var(--vora-bg-surface) 80%, transparent)', borderLeft: '1px solid var(--vora-border-subtle)' }}
                >
                    <div className="shrink-0 px-5 py-4 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)', borderBottom: '1px solid var(--vora-border-subtle)' }}>
                        Stations · {stations.length}
                    </div>
                    <div className="min-h-0 flex-1 space-y-1 overflow-y-auto p-2">
                        {stations.map(item => {
                            const isCurrent = item.id === station.id;
                            return (
                                <button
                                    key={item.id}
                                    type="button"
                                    onClick={() => onSelectStation(item)}
                                    aria-current={isCurrent}
                                    className="vora-row-interactive flex w-full cursor-pointer items-center gap-3 rounded-md border p-2 text-left"
                                    style={{
                                        background: isCurrent ? 'var(--vora-accent-soft)' : undefined,
                                        borderColor: isCurrent ? 'var(--vora-accent-soft-hover)' : 'transparent',
                                    }}
                                >
                                    <div
                                        className="flex h-10 w-10 shrink-0 items-center justify-center overflow-hidden rounded"
                                        style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                                    >
                                        {item.logoUrl ? <img src={item.logoUrl} alt="" className="h-full w-full object-contain" /> : null}
                                    </div>
                                    <div className="min-w-0 flex-1">
                                        <div className="truncate text-sm font-medium" style={{ color: isCurrent ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}>{item.name}</div>
                                        {item.groupTitle && <div className="truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{item.groupTitle}</div>}
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
