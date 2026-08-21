import { type AlbumVM, type TrackVM, type RadioSeed } from '../../../../api/Music/musicService';
import StarRating from '../../../../components/Client/Primitives/StarRating';

export interface AlbumTrackContextMenuPayload {
    x: number;
    y: number;
    track: TrackVM;
    index: number;
}

interface MusicAlbumViewProps {
    isLoading: boolean;
    currentAlbum: AlbumVM | null;
    tracks: TrackVM[];
    isServerAdmin: boolean;
    playFromIndex: (startIndex: number) => void;
    playWholeAlbum: () => void;
    startRadioFromSeed: (seed: RadioSeed) => Promise<void>;
    handleSetAlbumRating: (albumId: string, next: number | null) => Promise<void>;
    handleSetTrackRating: (trackId: string, next: number | null) => Promise<void>;
    toggleTrackLike: (trackId: string, currentlyLiked: boolean) => Promise<void>;
    onEditAlbum: (album: AlbumVM) => void;
    onEditTrack: (track: TrackVM) => void;
    onTrackContextMenu: (payload: AlbumTrackContextMenuPayload) => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicAlbumView({
    isLoading,
    currentAlbum,
    tracks,
    isServerAdmin,
    playFromIndex,
    playWholeAlbum,
    startRadioFromSeed,
    handleSetAlbumRating,
    handleSetTrackRating,
    toggleTrackLike,
    onEditAlbum,
    onEditTrack,
    onTrackContextMenu,
    formatDuration,
}: MusicAlbumViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading tracks...</div>;
    }
    if (!currentAlbum) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Album not found.</div>;
    }

    const displayArtist = currentAlbum.albumArtist || currentAlbum.artistName;
    const discMap = new Map<number, TrackVM[]>();
    tracks.forEach(t => {
        const d = t.discNumber ?? 1;
        if (!discMap.has(d)) discMap.set(d, []);
        discMap.get(d)!.push(t);
    });
    const discKeys = Array.from(discMap.keys()).sort((a, b) => a - b);
    const hasMultipleDiscs = discKeys.length > 1;
    const showTrackArtist = currentAlbum.isCompilation;

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-start gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="relative shrink-0" style={{ width: currentAlbum.discArtUrl ? '14rem' : '10rem', height: '10rem' }}>
                    {currentAlbum.discArtUrl && (
                        <div className="absolute top-0 right-0 w-40 h-40 rounded-full bg-black border border-[var(--vora-border-subtle)] overflow-hidden shadow-lg hidden sm:block">
                            <img src={currentAlbum.discArtUrl} alt="" className="w-full h-full object-cover" />
                        </div>
                    )}
                    <div className="relative w-40 h-40 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shadow-lg z-10 mx-auto sm:mx-0">
                        {currentAlbum.artworkUrl
                            ? <img src={currentAlbum.artworkUrl} alt={currentAlbum.title} className="max-w-full max-h-full object-cover" />
                            : <svg className="w-16 h-16 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                    </div>
                </div>
                <div className="flex-1 min-w-0 w-full">
                    <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
                        <div className="min-w-0">
                            {currentAlbum.isCompilation && (
                                <div className="text-xs uppercase tracking-widest text-[var(--vora-accent-text)] font-bold mb-1">Compilation</div>
                            )}
                            <h2 className="text-2xl sm:text-3xl font-bold text-[var(--vora-text-primary)] truncate">{currentAlbum.title}</h2>
                            <p className="text-base sm:text-lg text-[var(--vora-text-secondary)] truncate">{displayArtist}</p>
                            <p className="text-xs sm:text-sm text-[var(--vora-text-muted)] mt-1">{currentAlbum.year || ''}{currentAlbum.genre ? ` • ${currentAlbum.genre}` : ''}{tracks.length > 0 ? ` • ${tracks.length} tracks` : ''}</p>
                            <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 justify-center sm:justify-start">
                                <div className="flex items-center gap-2">
                                    <span className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Your rating</span>
                                    <StarRating
                                        value={currentAlbum.myRating ?? null}
                                        onChange={(next) => handleSetAlbumRating(currentAlbum.id, next)}
                                        showNumeric
                                    />
                                </div>
                                {currentAlbum.serverAdminRating != null && !isServerAdmin && (
                                    <div className="flex items-center gap-2">
                                        <span className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Server admin</span>
                                        <StarRating value={currentAlbum.serverAdminRating} readOnly showNumeric color="var(--vora-accent-text)" />
                                    </div>
                                )}
                            </div>
                        </div>
                        <div className="flex flex-wrap items-center gap-2 sm:justify-end shrink-0">
                            {tracks.length > 0 && (
                                <button
                                    type="button"
                                    onClick={playWholeAlbum}
                                    className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                    title="Play this album from the beginning"
                                >
                                    <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                    Play Album
                                </button>
                            )}
                            {currentAlbum.artistId && (
                                <button
                                    type="button"
                                    onClick={() => startRadioFromSeed({ seedKind: 'Artist', seedArtistId: currentAlbum.artistId })}
                                    className="text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                                    title="Start an endless radio station based on this artist"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                    Radio
                                </button>
                            )}
                            {isServerAdmin && (
                                <button
                                    type="button"
                                    onClick={() => onEditAlbum(currentAlbum)}
                                    className="text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                                    title="Edit album metadata"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                    Edit
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            </div>

            {discKeys.map(discNum => {
                const tracksInDisc = discMap.get(discNum)!;
                return (
                    <div key={discNum} className="mb-4">
                        {hasMultipleDiscs && (
                            <div className="flex items-center gap-2 mb-2 mt-4 first:mt-0">
                                <svg className="w-4 h-4 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 2a10 10 0 100 20 10 10 0 000-20zm0 14a4 4 0 110-8 4 4 0 010 8z" /></svg>
                                <span className="text-xs uppercase tracking-widest text-[var(--vora-text-muted)] font-bold">Disc {discNum}</span>
                                <div className="flex-1 h-px bg-[var(--vora-bg-surface)]" />
                            </div>
                        )}
                        <div className="space-y-1">
                            {tracksInDisc.map(track => {
                                const idx = tracks.indexOf(track);
                                return (
                                    <div
                                        key={track.id}
                                        onClick={() => playFromIndex(idx)}
                                        onContextMenu={(e) => {
                                            e.preventDefault();
                                            onTrackContextMenu({ x: e.pageX, y: e.pageY, track, index: idx });
                                        }}
                                        className="w-full text-left flex items-center gap-2 sm:gap-4 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                                    >
                                        <div className="w-6 sm:w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] shrink-0">
                                            <span className="group-hover:hidden">{track.trackNumber || '—'}</span>
                                            <svg className="w-5 h-5 hidden group-hover:inline-block text-[var(--vora-accent-text)]" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                                <span className="truncate">{track.title}</span>
                                                {track.contentRating && (
                                                    <span className="text-[10px] sm:text-xs font-bold uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] shrink-0">{track.contentRating}</span>
                                                )}
                                            </div>
                                            {showTrackArtist && track.artist && (
                                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{track.artist}</div>
                                            )}
                                        </div>
                                        <div
                                            onClick={(e) => e.stopPropagation()}
                                            className={`shrink-0 hidden sm:inline-flex transition-opacity ${track.myRating != null ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'}`}
                                        >
                                            <StarRating
                                                value={track.myRating ?? null}
                                                onChange={(next) => handleSetTrackRating(track.id, next)}
                                                size={13}
                                                ariaLabel={`Rate ${track.title}`}
                                            />
                                        </div>
                                        <button
                                            type="button"
                                            onClick={(e) => { e.stopPropagation(); toggleTrackLike(track.id, track.isLiked); }}
                                            className={`p-1.5 rounded transition-colors cursor-pointer shrink-0 ${track.isLiked ? 'text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-text)] opacity-100' : 'text-[var(--vora-text-disabled)] hover:text-[var(--vora-accent-text)] sm:opacity-0 sm:group-hover:opacity-100'}`}
                                            title={track.isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                                        >
                                            {track.isLiked
                                                ? <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                                                : <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" /></svg>}
                                        </button>
                                        <div className="text-xs text-[var(--vora-text-muted)] shrink-0 tabular-nums">{formatDuration(track.durationSeconds)}</div>
                                        {isServerAdmin && (
                                            <button
                                                type="button"
                                                onClick={(e) => { e.stopPropagation(); onEditTrack(track); }}
                                                className="p-1.5 rounded text-[var(--vora-text-disabled)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer shrink-0 hidden sm:inline-flex"
                                                title="Edit track"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                            </button>
                                        )}
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                );
            })}
        </>
    );
}
