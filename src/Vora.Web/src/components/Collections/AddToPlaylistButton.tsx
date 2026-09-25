import { useState } from 'react';
import AddToPlaylistModal, { type PlaylistItemKind } from './AddToPlaylistModal';

// The visible way to put a song in a playlist, on every track row. It used to
// be reachable only by right-clicking a track on an album page, so from the
// artist's Popular list, a mix or a phone there was no way at all.
//
// Shown on hover on a wide screen and always on a narrow one, where there is
// no hover. Opens its own dialog, so a row only has to drop it in.
interface AddToPlaylistButtonProps {
    mediaId: string;
    title: string;
    kind?: PlaylistItemKind;
}

export default function AddToPlaylistButton({ mediaId, title, kind = 'music' }: AddToPlaylistButtonProps) {
    const [open, setOpen] = useState(false);

    return (
        <>
            <button
                type="button"
                onClick={e => { e.stopPropagation(); setOpen(true); }}
                aria-label={`Add ${title} to a playlist`}
                title="Add to playlist"
                className="vora-icon-button shrink-0 cursor-pointer rounded-full p-1.5 opacity-100 transition-opacity sm:opacity-0 sm:group-hover:opacity-100 focus-visible:opacity-100"
                style={{ color: 'var(--vora-text-muted)' }}
            >
                <svg className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24" aria-hidden="true">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h11M4 12h11M4 18h7M18 14v6M15 17h6" />
                </svg>
            </button>
            {open && (
                <div onClick={e => e.stopPropagation()}>
                    <AddToPlaylistModal isOpen onClose={() => setOpen(false)} mediaId={mediaId} kind={kind} />
                </div>
            )}
        </>
    );
}
