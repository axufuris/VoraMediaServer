import { type ArtistTrackVM } from '../../../../api/Music/musicService';
import AddToPlaylistButton from '../../../../components/Collections/AddToPlaylistButton';
import ContentRatingBadge from '../../../../components/Media/ContentRatingBadge';

// The numbered, immediately playable track row. Written out twice already — in
// the Top Tracks view and the Mix view — and the artist page's Popular section
// would have been the third, so it becomes a component before that happens
// rather than after.
interface MusicTrackRowProps {
    track: ArtistTrackVM;
    position: number;
    onPlay: () => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicTrackRow({ track, position, onPlay, formatDuration }: MusicTrackRowProps) {
    return (
        <div
            onClick={onPlay}
            className="w-full text-left flex items-center gap-3 p-2 vora-row-interactive border border-transparent rounded transition-all cursor-pointer group"
        >
            <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums">{position}</div>
            <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                {track.albumArtworkUrl
                    ? <img src={track.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                    : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
            </div>
            <div className="flex-1 min-w-0">
                <div className="flex min-w-0 items-center gap-2 text-sm text-[var(--vora-text-primary)]">
                    <span className="truncate">{track.title}</span>
                    <ContentRatingBadge rating={track.contentRating} />
                </div>
                <div className="text-xs text-[var(--vora-text-muted)] truncate">{track.albumTitle ?? ''}</div>
            </div>
            <AddToPlaylistButton mediaId={track.id} title={track.title} />
            <div className="text-xs text-[var(--vora-text-muted)] w-12 text-right">{formatDuration(track.durationSeconds)}</div>
        </div>
    );
}
