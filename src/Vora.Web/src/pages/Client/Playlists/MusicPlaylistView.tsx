import type { ReactNode } from 'react';
import type { PlaylistDetailsVM } from '../../../api/Collections/playlistService';
import PlaylistCover from '../../../components/Collections/PlaylistCover';
import AddToPlaylistButton from '../../../components/Collections/AddToPlaylistButton';
import ContentRatingBadge from '../../../components/Media/ContentRatingBadge';

// A music playlist laid out like an album: its own cover, name and length at
// the top, and a numbered list where clicking a song plays from there.
//
// The film layout it used before headlined whichever item was selected, so the
// page's title was a song's name rather than the playlist's, and it carried
// watched checkmarks, Unwatch All and View Details - none of which mean
// anything for a song.
interface MusicPlaylistViewProps {
    playlist: PlaylistDetailsVM;
    canEdit: boolean;
    headerActions: ReactNode;
    onPlay: (startIndex: number, shuffle: boolean) => void;
    onRemove: (itemId: string) => void;
    draggedIndex: number | null;
    onDragStart: (e: React.DragEvent, index: number) => void;
    onDragOver: (e: React.DragEvent) => void;
    onDrop: (e: React.DragEvent, index: number) => void;
}

const formatDuration = (s?: number): string => {
    if (!s || s <= 0) return '';
    return `${Math.floor(s / 60)}:${Math.floor(s % 60).toString().padStart(2, '0')}`;
};

const formatTotal = (seconds: number): string => {
    const minutes = Math.round(seconds / 60);
    if (minutes < 60) return `${minutes} min`;
    return `${Math.floor(minutes / 60)} hr ${minutes % 60} min`;
};

export default function MusicPlaylistView({
    playlist, canEdit, headerActions, onPlay, onRemove, draggedIndex, onDragStart, onDragOver, onDrop,
}: MusicPlaylistViewProps) {
    const tracks = playlist.items;
    const totalSeconds = tracks.reduce((sum, t) => sum + (t.durationSeconds ?? 0), 0);
    const summary = [
        `${tracks.length} ${tracks.length === 1 ? 'song' : 'songs'}`,
        totalSeconds > 0 ? formatTotal(totalSeconds) : null,
    ].filter(Boolean).join(' • ');

    return (
        <>
            <div className="mb-10 flex flex-col items-center gap-6 sm:flex-row sm:items-end">
                <div className="w-44 shrink-0 sm:w-56">
                    <PlaylistCover imageUrl={playlist.imageUrl} posterUrls={playlist.posterUrls} shape="square" />
                </div>
                <div className="min-w-0 flex-1 text-center sm:text-left">
                    <div className="mb-1 text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Playlist</div>
                    <h1 className="text-3xl font-bold text-[var(--vora-text-primary)] sm:text-5xl">{playlist.name}</h1>
                    {playlist.description && (
                        <p className="mt-2 max-w-3xl text-sm text-[var(--vora-text-secondary)]">{playlist.description}</p>
                    )}
                    <p className="mt-2 text-sm text-[var(--vora-text-muted)]">{summary}</p>
                    <div className="mt-4 flex flex-wrap items-center justify-center gap-3 sm:justify-start">
                        {tracks.length > 0 && (
                            <>
                                <button
                                    type="button"
                                    onClick={() => onPlay(0, false)}
                                    className="vora-button-primary flex cursor-pointer items-center gap-2 rounded px-5 py-2 text-sm font-bold"
                                >
                                    <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14l11-7z" /></svg>
                                    Play
                                </button>
                                <button
                                    type="button"
                                    onClick={() => onPlay(0, true)}
                                    className="vora-pill flex cursor-pointer items-center gap-2 rounded px-4 py-2 text-sm font-semibold"
                                >
                                    <svg className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" d="M16 3h5v5M4 20L21 3M21 16v5h-5M15 15l6 6M4 4l5 5" /></svg>
                                    Shuffle
                                </button>
                            </>
                        )}
                        {headerActions}
                    </div>
                </div>
            </div>

            {tracks.length === 0 ? (
                <div className="rounded-lg border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] py-12 text-center text-sm text-[var(--vora-text-muted)]">
                    {canEdit
                        ? 'No songs yet. Add them with the playlist button beside any song.'
                        : 'Nothing in this playlist is available to you right now.'}
                </div>
            ) : (
                <ol className="space-y-1">
                    {tracks.map((track, index) => (
                        <li
                            key={track.id}
                            draggable={canEdit}
                            onDragStart={canEdit ? e => onDragStart(e, index) : undefined}
                            onDragOver={canEdit ? onDragOver : undefined}
                            onDrop={canEdit ? e => onDrop(e, index) : undefined}
                            onClick={() => onPlay(index, false)}
                            className={`vora-row-interactive group flex cursor-pointer items-center gap-3 rounded border border-transparent p-2 transition-all ${draggedIndex === index ? 'opacity-50' : ''}`}
                        >
                            <div className="w-6 shrink-0 text-right text-sm tabular-nums text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] sm:w-8">
                                {index + 1}
                            </div>
                            <div className="h-10 w-10 shrink-0 overflow-hidden rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-canvas)]">
                                {track.albumArtworkUrl && <img src={track.albumArtworkUrl} alt="" className="h-full w-full object-cover" />}
                            </div>
                            <div className="min-w-0 flex-1">
                                <div className="flex min-w-0 items-center gap-2 text-sm text-[var(--vora-text-primary)]">
                                    <span className="truncate">{track.title}</span>
                                    <ContentRatingBadge rating={track.contentRating} />
                                </div>
                                <div className="truncate text-xs text-[var(--vora-text-muted)]">
                                    {[track.artistName, track.albumTitle].filter(Boolean).join(' — ')}
                                </div>
                            </div>
                            <AddToPlaylistButton mediaId={track.mediaItemId} title={track.title} />
                            <div className="w-12 shrink-0 text-right text-xs tabular-nums text-[var(--vora-text-muted)]">{formatDuration(track.durationSeconds)}</div>
                            {canEdit && (
                                <button
                                    type="button"
                                    onClick={e => { e.stopPropagation(); onRemove(track.id); }}
                                    aria-label={`Remove ${track.title} from the playlist`}
                                    title="Remove from playlist"
                                    className="vora-icon-button shrink-0 cursor-pointer rounded-full p-1.5 text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-500)]"
                                >
                                    <svg className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24" aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
                                </button>
                            )}
                        </li>
                    ))}
                </ol>
            )}
        </>
    );
}
